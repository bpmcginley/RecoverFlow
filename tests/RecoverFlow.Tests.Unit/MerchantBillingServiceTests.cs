using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Billing;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;

namespace RecoverFlow.Tests.Unit;

public class MerchantBillingServiceTests
{
    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static Merchant SeedMerchant(AppDbContext db, int createdDaysAgo = 60, bool connected = true,
        string? platformCustomerId = null)
    {
        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            Email = "owner@acme.test",
            CompanyName = "Acme Corp",
            StripeAccountId = $"acct_{Guid.NewGuid():N}",
            EncryptedStripeAccessToken = connected ? "encrypted-token" : null,
            StripePlatformCustomerId = platformCustomerId,
            CreatedAt = DateTime.UtcNow.AddDays(-createdDaysAgo),
        };
        db.Merchants.Add(merchant);
        db.SaveChanges();
        return merchant;
    }

    private static FailedPayment SeedCase(AppDbContext db, Merchant merchant, long amountCents = 10_000,
        RecoveryStatus status = RecoveryStatus.Recovered, RecoveryMethod method = RecoveryMethod.SmartRetry,
        string currency = "usd")
    {
        var payment = new FailedPayment
        {
            Id = Guid.NewGuid(),
            MerchantId = merchant.Id,
            Merchant = merchant,
            StripeInvoiceId = $"in_{Guid.NewGuid():N}",
            AmountCents = amountCents,
            Currency = currency,
            Status = status,
            RecoveryMethod = method,
            FirstFailedAt = DateTime.UtcNow.AddDays(-10),
            RecoveredAt = status == RecoveryStatus.Recovered ? DateTime.UtcNow.AddDays(-2) : null,
        };
        db.FailedPayments.Add(payment);
        db.SaveChanges();
        return payment;
    }

    private static MerchantBillingService Service(AppDbContext db, FakePlatformFeeInvoicer invoicer, bool enabled = true) =>
        new(db, invoicer, Options.Create(new BillingOptions { Enabled = enabled }),
            NullLogger<MerchantBillingService>.Instance);

    [Fact]
    public async Task Fee_base_is_25_percent_of_attributable_recoveries_only()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 5); // in trial: no floor noise
        var billable = SeedCase(db, merchant, 10_000, RecoveryStatus.Recovered, RecoveryMethod.SmartRetry);
        var unknown = SeedCase(db, merchant, 50_000, RecoveryStatus.Recovered, RecoveryMethod.Unknown);
        SeedCase(db, merchant, 30_000, RecoveryStatus.ActiveRecovery);
        SeedCase(db, merchant, 40_000, RecoveryStatus.Lost);
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        var invoice = Assert.Single(db.FeeInvoices);
        Assert.Equal(10_000, invoice.BillableRecoveredCents);
        Assert.Equal(2_500, invoice.FeeCents);
        Assert.Equal(0, invoice.FloorTopUpCents);
        Assert.Equal(1, invoice.RecoveredCaseCount);
        Assert.Equal(invoice.Id, db.FailedPayments.Single(p => p.Id == billable.Id).FeeInvoiceId);
        Assert.Null(db.FailedPayments.Single(p => p.Id == unknown.Id).FeeInvoiceId);
    }

    [Fact]
    public async Task Floor_topup_applied_when_fee_below_minimum_after_trial()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 60);
        SeedCase(db, merchant, 10_000); // fee 2500 < 2900 floor
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        var invoice = Assert.Single(db.FeeInvoices);
        Assert.Equal(2_500, invoice.FeeCents);
        Assert.Equal(400, invoice.FloorTopUpCents);
        Assert.Equal(2_900, invoice.TotalCents);
        var (_, _, lines, _) = Assert.Single(invoicer.SendCalls);
        Assert.Equal(2, lines.Count); // fee line + top-up line
    }

    [Fact]
    public async Task Floor_waived_during_trial()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 29);
        SeedCase(db, merchant, 10_000);
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        var invoice = Assert.Single(db.FeeInvoices);
        Assert.Equal(0, invoice.FloorTopUpCents);
        Assert.Equal(2_500, invoice.TotalCents);
    }

    [Fact]
    public async Task Floor_applies_from_day_30_exactly()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 30); // CreatedAt + 30d is (just) in the past
        SeedCase(db, merchant, 10_000);
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        Assert.Equal(2_900, Assert.Single(db.FeeInvoices).TotalCents);
    }

    [Fact]
    public async Task No_topup_when_fee_meets_floor()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 60);
        SeedCase(db, merchant, 20_000); // fee 5000 >= 2900
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        var invoice = Assert.Single(db.FeeInvoices);
        Assert.Equal(5_000, invoice.FeeCents);
        Assert.Equal(0, invoice.FloorTopUpCents);
        Assert.Single(Assert.Single(invoicer.SendCalls).Lines);
    }

    [Fact]
    public async Task Trial_merchant_with_nothing_recovered_is_skipped()
    {
        using var db = CreateDb();
        SeedMerchant(db, createdDaysAgo: 5);
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        Assert.Empty(db.FeeInvoices);
        Assert.Empty(invoicer.SendCalls);
        Assert.Empty(invoicer.CustomerCalls);
    }

    [Fact]
    public async Task Posttrial_merchant_with_zero_recoveries_is_billed_the_floor()
    {
        using var db = CreateDb();
        SeedMerchant(db, createdDaysAgo: 60);
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        var invoice = Assert.Single(db.FeeInvoices);
        Assert.Equal(0, invoice.FeeCents);
        Assert.Equal(2_900, invoice.TotalCents);
        var line = Assert.Single(Assert.Single(invoicer.SendCalls).Lines);
        Assert.Contains("minimum", line.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Second_run_bills_nothing_new()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 5);
        SeedCase(db, merchant, 10_000);
        var invoicer = new FakePlatformFeeInvoicer();
        var service = Service(db, invoicer);

        await service.RunMonthlyBillingAsync();
        await service.RunMonthlyBillingAsync();

        Assert.Single(db.FeeInvoices);
        Assert.Single(invoicer.SendCalls);
    }

    [Fact]
    public async Task NonUsd_recoveries_are_excluded_and_left_unstamped()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 5);
        SeedCase(db, merchant, 10_000, currency: "usd");
        var eur = SeedCase(db, merchant, 99_000, currency: "eur");
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        Assert.Equal(10_000, Assert.Single(db.FeeInvoices).BillableRecoveredCents);
        Assert.Null(db.FailedPayments.Single(p => p.Id == eur.Id).FeeInvoiceId);
    }

    [Fact]
    public async Task Stripe_failure_marks_invoice_failed_and_keeps_cases_reserved()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 5);
        var payment = SeedCase(db, merchant, 10_000);
        var invoicer = new FakePlatformFeeInvoicer
        {
            NextSendResult = new(false, "in_orphan", null, "card_error"),
        };

        await Assert.ThrowsAsync<BillingRunIncompleteException>(
            () => Service(db, invoicer).RunMonthlyBillingAsync());

        var invoice = Assert.Single(db.FeeInvoices);
        Assert.Equal(FeeInvoiceStatus.Failed, invoice.Status);
        Assert.Equal("card_error", invoice.FailureReason);
        Assert.Equal("in_orphan", invoice.StripeInvoiceId); // persisted for resume
        var reserved = db.FailedPayments.Single(p => p.Id == payment.Id);
        Assert.Equal(invoice.Id, reserved.FeeInvoiceId);
        Assert.Null(reserved.BilledAtUtc);
    }

    [Fact]
    public async Task Failed_invoice_is_resumed_without_creating_a_new_one()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 5, platformCustomerId: "cus_existing");
        var payment = SeedCase(db, merchant, 10_000);
        var stuck = new FeeInvoice
        {
            Id = Guid.NewGuid(),
            MerchantId = merchant.Id,
            PeriodLabel = "2026-06",
            BillableRecoveredCents = 10_000,
            RecoveredCaseCount = 1,
            FeeCents = 2_500,
            TotalCents = 2_500,
            Status = FeeInvoiceStatus.Failed,
            StripeInvoiceId = "in_prev",
            FailureReason = "rate_limited",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
        };
        db.FeeInvoices.Add(stuck);
        payment.FeeInvoiceId = stuck.Id;
        db.SaveChanges();
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        var invoice = Assert.Single(db.FeeInvoices); // resumed, not duplicated
        Assert.Equal(stuck.Id, invoice.Id);
        Assert.Equal(FeeInvoiceStatus.Sent, invoice.Status);
        Assert.Null(invoice.FailureReason);
        Assert.NotNull(db.FailedPayments.Single(p => p.Id == payment.Id).BilledAtUtc);
        Assert.Equal("in_prev", Assert.Single(invoicer.SendCalls).KnownStripeInvoiceId);
    }

    [Fact]
    public async Task Platform_customer_created_lazily_and_persisted()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 60);
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        Assert.Equal("cus_fake", db.Merchants.Single(m => m.Id == merchant.Id).StripePlatformCustomerId);
        Assert.Single(invoicer.CustomerCalls);
    }

    [Fact]
    public async Task Existing_platform_customer_is_reused()
    {
        using var db = CreateDb();
        SeedMerchant(db, createdDaysAgo: 60, platformCustomerId: "cus_existing");
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        Assert.Empty(invoicer.CustomerCalls);
        Assert.Equal("cus_existing", Assert.Single(invoicer.SendCalls).CustomerId);
    }

    [Fact]
    public async Task Total_below_stripe_minimum_rolls_forward_unbilled()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 5);
        var tiny = SeedCase(db, merchant, 100); // fee 25¢ < 50¢ minimum
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        Assert.Empty(db.FeeInvoices);
        Assert.Null(db.FailedPayments.Single(p => p.Id == tiny.Id).FeeInvoiceId);
    }

    [Fact]
    public async Task One_merchant_failure_does_not_block_others()
    {
        using var db = CreateDb();
        var first = SeedMerchant(db, createdDaysAgo: 5);
        SeedCase(db, first, 10_000);
        var second = SeedMerchant(db, createdDaysAgo: 5);
        SeedCase(db, second, 10_000);
        var invoicer = new FakePlatformFeeInvoicer();
        invoicer.SendResults.Enqueue(new(false, null, null, "boom"));
        invoicer.SendResults.Enqueue(new(true, "in_ok", "https://invoice.example/ok", null));

        await Assert.ThrowsAsync<BillingRunIncompleteException>(
            () => Service(db, invoicer).RunMonthlyBillingAsync());

        Assert.Equal(2, db.FeeInvoices.Count());
        Assert.Equal(1, db.FeeInvoices.Count(f => f.Status == FeeInvoiceStatus.Sent));
        Assert.Equal(1, db.FeeInvoices.Count(f => f.Status == FeeInvoiceStatus.Failed));
    }

    [Fact]
    public async Task Disconnected_merchant_gets_no_new_billing()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 60, connected: false);
        SeedCase(db, merchant, 10_000);
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        Assert.Empty(db.FeeInvoices);
        Assert.Empty(invoicer.SendCalls);
    }

    [Fact]
    public async Task Disabled_flag_bills_nothing()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 60);
        SeedCase(db, merchant, 10_000);
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer, enabled: false).RunMonthlyBillingAsync();

        Assert.Empty(db.FeeInvoices);
        Assert.Empty(invoicer.SendCalls);
    }
}
