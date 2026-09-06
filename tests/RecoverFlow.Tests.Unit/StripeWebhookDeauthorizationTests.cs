using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Recovery;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;
using RecoverFlow.Infrastructure.Stripe;

namespace RecoverFlow.Tests.Unit;

/// <summary>
/// account.application.deauthorized is the only notice Stripe gives that an install is over.
/// Nothing clears the stored access token on an uninstall, so without this handler "connected"
/// is a latch that can only ever be set — which is what made the admin count wrong.
/// </summary>
public class StripeWebhookDeauthorizationTests
{
    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string htmlBody, string plainTextBody,
            string? trackingId = null, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private StripeWebhookProcessor Processor() => new(
        _db,
        new PaymentRecoveryService(
            _db,
            new DunningEmailService(_db, new NoOpEmailSender(), NullLogger<DunningEmailService>.Instance),
            new FakeRetryJobScheduler(),
            Options.Create(new RetryOptions()),
            NullLogger<PaymentRecoveryService>.Instance),
        TestTokens.Provider(_db),
        NullLogger<StripeWebhookProcessor>.Instance);

    private Merchant Seed(string accountId = "acct_123")
    {
        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            Email = "owner@acme.test",
            CompanyName = "Acme",
            StripeAccountId = accountId,
            EncryptedStripeAccessToken = "at_live",
            EncryptedStripeRefreshToken = "rt_live",
            StripeAccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow,
        };
        _db.Merchants.Add(merchant);
        _db.SaveChanges();
        return merchant;
    }

    // The data object is the application, not the account: the account id only ever arrives on
    // the envelope, which is why the handler reads it from there.
    private static string DeauthorizedEvent(string eventId, string? account) =>
        $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2024-06-20",
          "created": 1756800000,
          {{(account is null ? "" : $"\"account\": \"{account}\",")}}
          "type": "account.application.deauthorized",
          "data": {
            "object": {
              "id": "ca_test",
              "object": "application",
              "name": "RecoverFlow"
            }
          }
        }
        """;

    [Fact]
    public async Task An_uninstall_marks_the_merchant_disconnected()
    {
        Seed();

        await Processor().ProcessAsync(DeauthorizedEvent("evt_1", "acct_123"));

        Assert.NotNull((await _db.Merchants.SingleAsync()).DisconnectedAtUtc);
    }

    [Fact]
    public async Task The_dead_tokens_are_left_in_place()
    {
        // They stopped working the moment the grant went; wiping them would only cost us the
        // ability to say what a merchant had connected when they ask.
        Seed();

        await Processor().ProcessAsync(DeauthorizedEvent("evt_2", "acct_123"));

        var saved = await _db.Merchants.SingleAsync();
        Assert.Equal("at_live", saved.EncryptedStripeAccessToken);
        Assert.Equal("rt_live", saved.EncryptedStripeRefreshToken);
    }

    [Fact]
    public async Task An_uninstall_for_an_account_we_do_not_know_is_ignored()
    {
        // Accounts that authorized the read-only audit and never installed the app have no row.
        var merchant = Seed();

        await Processor().ProcessAsync(DeauthorizedEvent("evt_3", "acct_someone_else"));

        Assert.Null((await _db.Merchants.SingleAsync(m => m.Id == merchant.Id)).DisconnectedAtUtc);
    }

    [Fact]
    public async Task An_event_naming_no_account_is_ignored()
    {
        Seed();

        await Processor().ProcessAsync(DeauthorizedEvent("evt_4", null));

        Assert.Null((await _db.Merchants.SingleAsync()).DisconnectedAtUtc);
    }

    [Fact]
    public async Task A_redelivered_uninstall_is_deduped_like_every_other_event()
    {
        Seed();
        var processor = Processor();

        await processor.ProcessAsync(DeauthorizedEvent("evt_5", "acct_123"));
        await processor.ProcessAsync(DeauthorizedEvent("evt_5", "acct_123"));

        Assert.Single(await _db.ProcessedWebhookEvents.ToListAsync());
    }
}
