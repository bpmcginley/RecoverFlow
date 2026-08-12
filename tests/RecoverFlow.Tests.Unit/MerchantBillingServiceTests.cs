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
    public async Task Fee_is_capped_at_the_published_monthly_ceiling()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 60);
        SeedCase(db, merchant, 500_000); // 25% would be $1,250
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        var invoice = Assert.Single(db.FeeInvoices);
        Assert.Equal(500_000, invoice.BillableRecoveredCents);
        Assert.Equal(29_900, invoice.FeeCents);
        Assert.Equal(0, invoice.FloorTopUpCents);
        Assert.Equal(29_900, invoice.TotalCents);

        // Lines must still sum to the total, or Stripe rejects the invoice.
        var (_, _, lines, _) = Assert.Single(invoicer.SendCalls);
        Assert.Equal(29_900, lines.Sum(l => l.AmountCents));
        Assert.Contains("capped", Assert.Single(lines).Description);
    }

    [Fact]
    public async Task Cap_does_not_bite_just_below_the_ceiling()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 60);
        SeedCase(db, merchant, 119_600); // 25% is exactly $299, the ceiling itself
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        var invoice = Assert.Single(db.FeeInvoices);
        Assert.Equal(29_900, invoice.TotalCents);
        Assert.DoesNotContain("capped", Assert.Single(Assert.Single(invoicer.SendCalls).Item3).Description);
    }

    // --- Reversals: refunds and chargebacks --------------------------------------------------
    // The rule these cover: we never keep a fee on revenue the merchant did not keep.

    /// <summary>Marks a case as refunded/charged back the way the webhook handler would.</summary>
    private static void Reverse(AppDbContext db, FailedPayment payment, long reversedCents, string reason = "refund")
    {
        payment.ReversedAmountCents = reversedCents;
        payment.ReversedAtUtc ??= DateTime.UtcNow;
        payment.ReversalReason = reason;
        db.SaveChanges();
    }

    [Fact]
    public async Task Recovery_reversed_before_billing_is_never_billed_at_all()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 5); // in trial, so no floor to hide behind
        var charged_back = SeedCase(db, merchant, 40_000);
        Reverse(db, charged_back, 40_000, "dispute");
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        Assert.Empty(db.FeeInvoices);
        Assert.Empty(invoicer.SendCalls);
        Assert.Null(db.FailedPayments.Single().FeeInvoiceId);
    }

    [Fact]
    public async Task Partly_refunded_recovery_bills_on_what_the_merchant_kept()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 5);
        var payment = SeedCase(db, merchant, 40_000);
        Reverse(db, payment, 10_000); // $100 handed back, $300 kept
        var invoicer = new FakePlatformFeeInvoicer();

        await Service(db, invoicer).RunMonthlyBillingAsync();

        var invoice = Assert.Single(db.FeeInvoices);
        Assert.Equal(30_000, invoice.BillableRecoveredCents);
        Assert.Equal(7_500, invoice.TotalCents);
        Assert.Equal(30_000, db.FailedPayments.Single().BilledBaseCents);
    }

    [Fact]
    public async Task Fee_for_a_recovery_reversed_after_billing_comes_back_on_the_next_invoice()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 60, platformCustomerId: "cus_x");
        var reversed = SeedCase(db, merchant, 40_000); // fee $100
        var invoicer = new FakePlatformFeeInvoicer();
        var svc = Service(db, invoicer);
        await svc.RunMonthlyBillingAsync();
        Assert.Equal(10_000, db.FeeInvoices.Single().TotalCents);

        Reverse(db, reversed, 40_000);
        SeedCase(db, merchant, 60_000); // next month: fee $150, so the $100 credit fits
        await svc.RunMonthlyBillingAsync();

        var second = db.FeeInvoices.Single(f => f.ReversalCreditCents > 0);
        Assert.Equal(15_000, second.FeeCents);
        Assert.Equal(10_000, second.ReversalCreditCents);
        Assert.Equal(5_000, second.TotalCents);

        // Lines must still sum to the total, or Stripe rejects the invoice.
        var lines = invoicer.SendCalls.Last().Lines;
        Assert.Equal(5_000, lines.Sum(l => l.AmountCents));
        Assert.Contains(lines, l => l.AmountCents == -10_000);
        Assert.Equal(10_000, db.FailedPayments.Single(p => p.Id == reversed.Id).ReversalCreditedCents);
    }

    [Fact]
    public async Task Credit_too_big_for_one_month_finishes_on_the_month_after()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 60, platformCustomerId: "cus_x");
        var big = SeedCase(db, merchant, 400_000); // 25% is $1,000 but the cap charges $299
        var invoicer = new FakePlatformFeeInvoicer();
        var svc = Service(db, invoicer);
        await svc.RunMonthlyBillingAsync();
        Assert.Equal(29_900, db.FeeInvoices.Single().TotalCents);

        // Charged back in full: we owe back the $299 we actually took, not 25% of $4,000.
        Reverse(db, big, 400_000, "dispute");
        SeedCase(db, merchant, 40_000); // fee $100 + floor 0 => only $100 of the credit fits
        await svc.RunMonthlyBillingAsync();

        var stamped = db.FailedPayments.Single(p => p.Id == big.Id);
        Assert.Equal(29_900, stamped.BilledFeeCents);
        Assert.Equal(10_000, stamped.ReversalCreditedCents);

        // Month two owes nothing, so Stripe is never called: only month one was ever sent. The
        // month is still recorded, or the credit it spent would be spendable a second time.
        Assert.Single(invoicer.SendCalls);
        var settled = db.FeeInvoices.Single(f => f.ReversalCreditCents == 10_000);
        Assert.Equal(0, settled.TotalCents);
        Assert.Equal(FeeInvoiceStatus.Sent, settled.Status);

        // A third month with nothing recovered: the floor is billed and the rest of the credit
        // lands on it, so the leftover cannot sit on the account forever.
        await svc.RunMonthlyBillingAsync();
        Assert.Equal(12_900, db.FailedPayments.Single(p => p.Id == big.Id).ReversalCreditedCents);
        Assert.Single(invoicer.SendCalls);
    }

    [Fact]
    public async Task Reversal_of_the_part_that_was_never_billed_is_not_credited_again()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 5); // trial: no floor in the arithmetic
        var payment = SeedCase(db, merchant, 40_000);
        Reverse(db, payment, 10_000); // refunded before billing: bills on $300, fee $75
        var invoicer = new FakePlatformFeeInvoicer();
        var svc = Service(db, invoicer);
        await svc.RunMonthlyBillingAsync();
        Assert.Equal(7_500, db.FeeInvoices.Single().TotalCents);

        Reverse(db, payment, 40_000); // the rest goes back later
        SeedCase(db, merchant, 40_000);
        await svc.RunMonthlyBillingAsync();

        // Only the $300 we actually charged for is credited: $75, not $100.
        Assert.Equal(7_500, db.FailedPayments.Single(p => p.Id == payment.Id).ReversalCreditedCents);
        Assert.Equal(2_500, invoicer.SendCalls.Last().Lines.Sum(l => l.AmountCents)); // $100 fee less $75
    }

    [Fact]
    public async Task Credit_is_split_across_the_cases_that_earned_the_fee()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db, createdDaysAgo: 5);
        var a = SeedCase(db, merchant, 30_000);
        var b = SeedCase(db, merchant, 10_000);
        var invoicer = new FakePlatformFeeInvoicer();
        await Service(db, invoicer).RunMonthlyBillingAsync();

        // $400 recovered, $100 fee, split 3:1 by what each case contributed.
        Assert.Equal(7_500, db.FailedPayments.Single(p => p.Id == a.Id).BilledFeeCents);
        Assert.Equal(2_500, db.FailedPayments.Single(p => p.Id == b.Id).BilledFeeCents);
        Assert.Equal(10_000, db.FailedPayments.Sum(p => p.BilledFeeCents));
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
