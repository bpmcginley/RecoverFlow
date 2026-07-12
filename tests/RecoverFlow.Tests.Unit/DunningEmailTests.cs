using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Recovery;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;

namespace RecoverFlow.Tests.Unit;

public class DunningEmailTemplatesTests
{
    [Theory]
    [InlineData(1, "friendly_reminder")]
    [InlineData(2, "urgent")]
    [InlineData(3, "final_notice")]
    [InlineData(7, "final_notice")]
    public void ForStep_maps_step_to_email_type(int step, string expectedType)
    {
        var content = DunningEmailTemplates.ForStep(step, "Acme", 4200, "usd");

        Assert.Equal(expectedType, content.EmailType);
    }

    [Fact]
    public void ForStep_interpolates_merchant_amount_and_currency()
    {
        var content = DunningEmailTemplates.ForStep(1, "Acme Corp", 4200, "usd");

        Assert.Contains("Acme Corp", content.Subject);
        Assert.Contains("42.00 USD", content.Html);
        Assert.Contains("Acme Corp", content.Html);
    }

    [Fact]
    public void ForStep_html_encodes_merchant_name_in_body()
    {
        var content = DunningEmailTemplates.ForStep(1, "Bits & Bobs <Ltd>", 999, "gbp");

        Assert.Contains("Bits &amp; Bobs &lt;Ltd&gt;", content.Html);
        Assert.DoesNotContain("<Ltd>", content.Html);
    }
}

public class DunningEmailServiceTests
{
    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Html, string PlainText)> Sent { get; } = [];

        public Task SendAsync(string to, string subject, string htmlBody, string plainTextBody, CancellationToken ct = default)
        {
            Sent.Add((to, subject, htmlBody, plainTextBody));
            return Task.CompletedTask;
        }
    }

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static FailedPayment SeedPayment(AppDbContext db, string? customerEmail = "customer@example.com")
    {
        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            Email = "owner@acme.test",
            CompanyName = "Acme Corp",
            StripeAccountId = $"acct_{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
        };
        var payment = new FailedPayment
        {
            Id = Guid.NewGuid(),
            MerchantId = merchant.Id,
            Merchant = merchant,
            StripeInvoiceId = $"in_{Guid.NewGuid():N}",
            CustomerEmail = customerEmail,
            AmountCents = 4200,
            Currency = "usd",
            Status = RecoveryStatus.ActiveRecovery,
            FirstFailedAt = DateTime.UtcNow,
        };
        db.FailedPayments.Add(payment);
        db.SaveChanges();
        return payment;
    }

    private static DunningEmailService CreateService(AppDbContext db, RecordingEmailSender sender) =>
        new(db, sender, NullLogger<DunningEmailService>.Instance);

    [Fact]
    public async Task SendStepAsync_sends_email_and_records_entry()
    {
        using var db = CreateDb();
        var payment = SeedPayment(db);
        var sender = new RecordingEmailSender();

        await CreateService(db, sender).SendStepAsync(payment.Id, 1);

        var (to, subject, html, plain) = Assert.Single(sender.Sent);
        Assert.Equal("customer@example.com", to);
        Assert.Contains("Acme Corp", subject);
        Assert.Contains("42.00 USD", html);
        Assert.Contains("42.00 USD", plain);
        Assert.DoesNotContain("<", plain); // genuine plain-text alternative, no markup

        var entry = Assert.Single(db.EmailSequences);
        Assert.Equal(payment.Id, entry.FailedPaymentId);
        Assert.Equal(1, entry.SequenceStep);
        Assert.Equal("friendly_reminder", entry.EmailType);
        Assert.NotEqual(default, entry.SentAt);
    }

    [Fact]
    public async Task SendStepAsync_is_idempotent_per_step()
    {
        using var db = CreateDb();
        var payment = SeedPayment(db);
        var sender = new RecordingEmailSender();
        var service = CreateService(db, sender);

        await service.SendStepAsync(payment.Id, 1);
        await service.SendStepAsync(payment.Id, 1);
        await service.SendStepAsync(payment.Id, 2);

        Assert.Equal(2, sender.Sent.Count);
        Assert.Equal([1, 2], db.EmailSequences.Select(e => e.SequenceStep).OrderBy(s => s).ToList());
    }

    [Fact]
    public async Task SendStepAsync_skips_when_customer_email_missing()
    {
        using var db = CreateDb();
        var payment = SeedPayment(db, customerEmail: null);
        var sender = new RecordingEmailSender();

        await CreateService(db, sender).SendStepAsync(payment.Id, 1);

        Assert.Empty(sender.Sent);
        Assert.Empty(db.EmailSequences);
    }

    [Fact]
    public async Task SendStepAsync_skips_closed_cases()
    {
        using var db = CreateDb();
        var payment = SeedPayment(db);
        payment.Status = RecoveryStatus.Recovered;
        db.SaveChanges();
        var sender = new RecordingEmailSender();

        await CreateService(db, sender).SendStepAsync(payment.Id, 2);

        Assert.Empty(sender.Sent);
        Assert.Empty(db.EmailSequences);
    }
}
