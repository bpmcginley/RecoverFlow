using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Billing;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;
using RecoverFlow.Infrastructure.Stripe;
using Xunit.Abstractions;

namespace RecoverFlow.Tests.Integration;

/// <summary>
/// Manual live pass: drives the real StripePlatformFeeInvoicer against Stripe TEST mode
/// (customer → draft invoice → line items → send) so we can eyeball a real hosted invoice
/// before enabling billing in production. No-ops (soft skip) unless a test key is provided
/// via env var STRIPE_TEST_SECRET_KEY or the file %USERPROFILE%\.rf_stripe_test_key.txt.
/// </summary>
public sealed class PlatformFeeInvoicerLiveTests(ITestOutputHelper output)
{
    private static string? ResolveTestKey()
    {
        var fromEnv = Environment.GetEnvironmentVariable("STRIPE_TEST_SECRET_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

        var file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rf_stripe_test_key.txt");
        return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
    }

    [Fact]
    public async Task Bills_a_recovered_case_end_to_end_against_stripe_test_mode()
    {
        var key = ResolveTestKey();
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("sk_test_", StringComparison.Ordinal))
        {
            output.WriteLine("SKIPPED: no Stripe test key (set STRIPE_TEST_SECRET_KEY or ~/.rf_stripe_test_key.txt with sk_test_...).");
            return;
        }
        global::Stripe.StripeConfiguration.ApiKey = key;

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using var db = new AppDbContext(options);
        db.Database.EnsureCreated();

        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            Email = "billing-test@recoverflow.org",
            CompanyName = "RecoverFlow Live Test Co",
            StripeAccountId = $"acct_{Guid.NewGuid():N}",
            EncryptedStripeAccessToken = "encrypted-token",
            CreatedAt = DateTime.UtcNow.AddDays(-60), // past trial → floor logic active
        };
        db.Merchants.Add(merchant);
        db.FailedPayments.Add(new FailedPayment
        {
            Id = Guid.NewGuid(),
            MerchantId = merchant.Id,
            StripeInvoiceId = $"in_{Guid.NewGuid():N}",
            AmountCents = 10_000, // $100 recovered → $25 fee, floor tops up to $29
            Currency = "usd",
            Status = RecoveryStatus.Recovered,
            RecoveryMethod = RecoveryMethod.SmartRetry,
            FirstFailedAt = DateTime.UtcNow.AddDays(-10),
            RecoveredAt = DateTime.UtcNow.AddDays(-2),
        });
        await db.SaveChangesAsync();

        var service = new MerchantBillingService(
            db,
            new StripePlatformFeeInvoicer(db),
            Options.Create(new BillingOptions { Enabled = true }),
            NullLogger<MerchantBillingService>.Instance);

        await service.RunMonthlyBillingAsync();

        var invoice = await db.FeeInvoices.SingleAsync();
        var report =
            $"FeeInvoice status:   {invoice.Status}\n" +
            $"Fee / floor / total: {invoice.FeeCents} / {invoice.FloorTopUpCents} / {invoice.TotalCents} cents\n" +
            $"Stripe customer:     {merchant.StripePlatformCustomerId}\n" +
            $"Stripe invoice:      {invoice.StripeInvoiceId}\n" +
            $"Hosted invoice URL:  {invoice.HostedInvoiceUrl}\n" +
            $"Failure reason:      {invoice.FailureReason ?? "(none)"}\n";
        output.WriteLine(report);
        // Also drop to a file so the result is readable regardless of pass/fail.
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "rf-billing-live-result.txt"), report);

        Assert.Equal(FeeInvoiceStatus.Sent, invoice.Status);
        Assert.Equal(2_500, invoice.FeeCents);
        Assert.Equal(400, invoice.FloorTopUpCents);
        Assert.Equal(2_900, invoice.TotalCents);
        Assert.StartsWith("cus_", merchant.StripePlatformCustomerId);
        Assert.StartsWith("in_", invoice.StripeInvoiceId);
        Assert.False(string.IsNullOrEmpty(invoice.HostedInvoiceUrl));
        Assert.NotNull(await db.FailedPayments.Where(p => p.BilledAtUtc != null).SingleOrDefaultAsync());
    }
}
