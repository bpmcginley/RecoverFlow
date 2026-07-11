using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Recovery;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;

namespace RecoverFlow.Tests.Unit;

/// <summary>Covers the Hangfire-scheduling side of case opening; retry timing itself is RetrySchedulerTests.</summary>
public class PaymentRecoverySchedulingTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);
    private readonly FakeRetryJobScheduler _scheduler = new();

    private PaymentRecoveryService Service() => new(
        _db, _scheduler,
        Options.Create(new RetryOptions()),
        NullLogger<PaymentRecoveryService>.Instance);

    private static PaymentFailedEvent Failed(string declineCode) => new(
        "acct_123", "in_123", "sub_123", "cus_123", "customer@acme.test",
        2900, "usd", declineCode, "declined", DateTime.UtcNow);

    private void SeedMerchant()
    {
        _db.Merchants.Add(new Merchant
        {
            Id = Guid.NewGuid(),
            Email = "owner@acme.test",
            CompanyName = "Acme",
            StripeAccountId = "acct_123",
            CreatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Opening_a_case_schedules_the_first_retry_job()
    {
        SeedMerchant();

        await Service().HandlePaymentFailedAsync(Failed("card_declined"));

        var payment = _db.FailedPayments.Include(p => p.RetryAttempts).Single();
        var attempt = Assert.Single(payment.RetryAttempts);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal((attempt.Id, attempt.ScheduledFor), Assert.Single(_scheduler.Scheduled));
    }

    [Fact]
    public async Task Hard_decline_opens_the_case_but_schedules_nothing()
    {
        SeedMerchant();

        await Service().HandlePaymentFailedAsync(Failed("stolen_card"));

        Assert.Empty(_db.RetryAttempts);
        Assert.Empty(_scheduler.Scheduled);
    }

    [Fact]
    public async Task Repeat_failure_with_a_pending_attempt_does_not_stack_a_second_job()
    {
        SeedMerchant();
        var svc = Service();

        // Our own failed retry re-triggers invoice.payment_failed on the same invoice.
        await svc.HandlePaymentFailedAsync(Failed("card_declined"));
        await svc.HandlePaymentFailedAsync(Failed("card_declined"));

        Assert.Equal(1, _db.RetryAttempts.Count());
        Assert.Single(_scheduler.Scheduled);
    }
}
