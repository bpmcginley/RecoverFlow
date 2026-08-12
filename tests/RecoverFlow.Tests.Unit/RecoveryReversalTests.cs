using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Recovery;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;

namespace RecoverFlow.Tests.Unit;

/// <summary>
/// Recording refunds and chargebacks against a recovery we already claimed. Billing reads what
/// these write; the money consequences of it are in MerchantBillingServiceTests.
/// </summary>
public class RecoveryReversalTests
{
    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string htmlBody, string plainTextBody, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private PaymentRecoveryService Service() => new(
        _db,
        new DunningEmailService(_db, new NoOpEmailSender(), NullLogger<DunningEmailService>.Instance),
        new FakeRetryJobScheduler(),
        Options.Create(new RetryOptions()),
        NullLogger<PaymentRecoveryService>.Instance);

    private FailedPayment Seed(string? chargeId = "ch_1", RecoveryStatus status = RecoveryStatus.Recovered)
    {
        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            Email = "owner@acme.test",
            CompanyName = "Acme",
            StripeAccountId = "acct_123",
            CreatedAt = DateTime.UtcNow,
        };
        var payment = new FailedPayment
        {
            Id = Guid.NewGuid(),
            MerchantId = merchant.Id,
            Merchant = merchant,
            StripeInvoiceId = "in_1",
            StripeChargeId = chargeId,
            AmountCents = 40_000,
            Currency = "usd",
            Status = status,
            RecoveryMethod = RecoveryMethod.SmartRetry,
            FirstFailedAt = DateTime.UtcNow.AddDays(-10),
            RecoveredAt = DateTime.UtcNow.AddDays(-2),
        };
        _db.Merchants.Add(merchant);
        _db.FailedPayments.Add(payment);
        _db.SaveChanges();
        return payment;
    }

    private static RecoveryReversedEvent Reversed(long cents, string reason = "refund", string chargeId = "ch_1") =>
        new("acct_123", chargeId, cents, reason, DateTime.UtcNow);

    [Fact]
    public async Task Refund_of_a_recovery_is_recorded_against_the_case()
    {
        Seed();

        await Service().HandleRecoveryReversedAsync(Reversed(40_000));

        var payment = _db.FailedPayments.Single();
        Assert.Equal(40_000, payment.ReversedAmountCents);
        Assert.Equal("refund", payment.ReversalReason);
        Assert.NotNull(payment.ReversedAtUtc);
    }

    [Fact]
    public async Task Chargeback_is_recorded_the_same_way_as_a_refund()
    {
        Seed();

        await Service().HandleRecoveryReversedAsync(Reversed(40_000, "dispute"));

        Assert.Equal("dispute", _db.FailedPayments.Single().ReversalReason);
    }

    [Fact]
    public async Task Stripe_reports_refunds_as_a_running_total_so_two_events_do_not_double_up()
    {
        Seed();
        var svc = Service();

        await svc.HandleRecoveryReversedAsync(Reversed(10_000));
        await svc.HandleRecoveryReversedAsync(Reversed(25_000)); // $250 total, not another $250

        Assert.Equal(25_000, _db.FailedPayments.Single().ReversedAmountCents);
    }

    [Fact]
    public async Task A_redelivered_event_changes_nothing()
    {
        var payment = Seed();
        var svc = Service();
        await svc.HandleRecoveryReversedAsync(Reversed(10_000));
        var firstSeen = _db.FailedPayments.Single().ReversedAtUtc;

        await svc.HandleRecoveryReversedAsync(Reversed(10_000));

        var after = _db.FailedPayments.Single(p => p.Id == payment.Id);
        Assert.Equal(10_000, after.ReversedAmountCents);
        Assert.Equal(firstSeen, after.ReversedAtUtc);
    }

    [Fact]
    public async Task Reversal_can_never_exceed_the_amount_we_claimed()
    {
        Seed();

        // A dispute carries the card-network fee, so its amount can top the original charge.
        await Service().HandleRecoveryReversedAsync(Reversed(41_500, "dispute"));

        Assert.Equal(40_000, _db.FailedPayments.Single().ReversedAmountCents);
    }

    [Fact]
    public async Task A_refund_on_a_charge_that_was_never_ours_is_ignored()
    {
        Seed(chargeId: "ch_ours");

        await Service().HandleRecoveryReversedAsync(Reversed(40_000, chargeId: "ch_someone_else"));

        Assert.Equal(0, _db.FailedPayments.Single().ReversedAmountCents);
    }

    [Fact]
    public async Task A_case_we_never_recovered_is_left_alone()
    {
        Seed(status: RecoveryStatus.ActiveRecovery);

        await Service().HandleRecoveryReversedAsync(Reversed(40_000));

        Assert.Null(_db.FailedPayments.Single().ReversedAtUtc);
    }

    [Fact]
    public async Task The_charge_id_is_captured_when_the_invoice_is_paid_so_a_refund_can_find_it()
    {
        var payment = Seed(chargeId: null, status: RecoveryStatus.ActiveRecovery);
        var svc = Service();

        await svc.HandleInvoicePaidAsync(new InvoicePaidEvent("acct_123", "in_1", "ch_new", DateTime.UtcNow));
        await svc.HandleRecoveryReversedAsync(Reversed(40_000, chargeId: "ch_new"));

        var after = _db.FailedPayments.Single(p => p.Id == payment.Id);
        Assert.Equal("ch_new", after.StripeChargeId);
        Assert.Equal(40_000, after.ReversedAmountCents);
    }
}
