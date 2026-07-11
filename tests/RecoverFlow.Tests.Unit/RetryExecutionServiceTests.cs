using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Recovery;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;

namespace RecoverFlow.Tests.Unit;

public class RetryExecutionServiceTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);
    private readonly FakeInvoicePayer _payer = new();
    private readonly FakeRetryJobScheduler _scheduler = new();

    private RetryExecutionService Service(int maxAttempts = 4, int maxDays = 14) => new(
        _db, _payer, _scheduler,
        Options.Create(new RetryOptions { MaxRetryAttempts = maxAttempts, MaxDaysToRecover = maxDays }),
        NullLogger<RetryExecutionService>.Instance);

    private (FailedPayment Payment, RetryAttempt Attempt) Seed(
        RecoveryStatus status = RecoveryStatus.ActiveRecovery,
        int attemptNumber = 1,
        DateTime? firstFailedAt = null)
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
            StripeInvoiceId = "in_123",
            AmountCents = 2900,
            Currency = "usd",
            DeclineCode = "generic_decline",
            Status = status,
            FirstFailedAt = firstFailedAt ?? DateTime.UtcNow.AddHours(-6),
        };
        var attempt = new RetryAttempt
        {
            Id = Guid.NewGuid(),
            FailedPaymentId = payment.Id,
            AttemptNumber = attemptNumber,
            ScheduledFor = DateTime.UtcNow,
        };
        payment.RetryAttempts.Add(attempt);
        _db.Merchants.Add(merchant);
        _db.FailedPayments.Add(payment);
        _db.SaveChanges();
        return (payment, attempt);
    }

    [Fact]
    public async Task Success_marks_attempt_and_leaves_case_open_for_the_webhook_to_close()
    {
        var (payment, attempt) = Seed();

        await Service().ExecuteAsync(attempt.Id);

        Assert.Equal("success", attempt.Result);
        Assert.NotNull(attempt.AttemptedAt);
        Assert.Equal(RecoveryStatus.ActiveRecovery, payment.Status); // invoice.paid webhook closes it
        Assert.Empty(_scheduler.Scheduled);

        var call = Assert.Single(_payer.Calls);
        Assert.Equal(("in_123", "acct_123", attempt.Id.ToString()), call);
    }

    [Fact]
    public async Task Failure_records_decline_and_schedules_the_next_attempt()
    {
        var (payment, attempt) = Seed();
        _payer.NextResult = InvoicePayResult.Declined("card_declined", "Your card was declined.");

        await Service().ExecuteAsync(attempt.Id);

        Assert.Equal("failed", attempt.Result);
        Assert.Equal("card_declined", attempt.DeclineCodeReceived);
        Assert.Equal(RecoveryStatus.ActiveRecovery, payment.Status);

        var next = Assert.Single(payment.RetryAttempts, a => a.Result is null);
        Assert.Equal(2, next.AttemptNumber);
        Assert.Equal((next.Id, next.ScheduledFor), Assert.Single(_scheduler.Scheduled));
    }

    [Fact]
    public async Task Exhausted_attempt_count_marks_the_case_lost()
    {
        var (payment, attempt) = Seed(attemptNumber: 2);
        _payer.NextResult = InvoicePayResult.Declined("card_declined", "declined");

        await Service(maxAttempts: 2).ExecuteAsync(attempt.Id);

        Assert.Equal(RecoveryStatus.Lost, payment.Status);
        Assert.NotNull(payment.LostAt);
        Assert.Empty(_scheduler.Scheduled);
    }

    [Fact]
    public async Task Hard_decline_on_retry_stops_and_marks_the_case_lost()
    {
        var (payment, attempt) = Seed();
        _payer.NextResult = InvoicePayResult.Declined("stolen_card", "declined");

        await Service().ExecuteAsync(attempt.Id);

        Assert.Equal(RecoveryStatus.Lost, payment.Status);
        Assert.Empty(_scheduler.Scheduled);
    }

    [Fact]
    public async Task Card_update_decline_stops_retries_but_keeps_the_case_open()
    {
        var (payment, attempt) = Seed();
        _payer.NextResult = InvoicePayResult.Declined("expired_card", "expired");

        await Service().ExecuteAsync(attempt.Id);

        Assert.Equal(RecoveryStatus.ActiveRecovery, payment.Status); // card-update flow may still recover it
        Assert.Empty(_scheduler.Scheduled);
    }

    [Fact]
    public async Task Recovery_window_expiry_marks_the_case_lost()
    {
        var (payment, attempt) = Seed(firstFailedAt: DateTime.UtcNow.AddDays(-15));
        _payer.NextResult = InvoicePayResult.Declined("card_declined", "declined");

        await Service(maxDays: 14).ExecuteAsync(attempt.Id);

        Assert.Equal(RecoveryStatus.Lost, payment.Status);
        Assert.Empty(_scheduler.Scheduled);
    }

    [Fact]
    public async Task Resolved_case_skips_the_attempt_without_calling_stripe()
    {
        var (_, attempt) = Seed(status: RecoveryStatus.Recovered);

        await Service().ExecuteAsync(attempt.Id);

        Assert.Equal("skipped", attempt.Result);
        Assert.Null(attempt.AttemptedAt);
        Assert.Empty(_payer.Calls);
    }

    [Fact]
    public async Task Finished_attempt_is_never_rerun()
    {
        var (_, attempt) = Seed();
        attempt.Result = "success";
        _db.SaveChanges();

        await Service().ExecuteAsync(attempt.Id); // Hangfire at-least-once redelivery

        Assert.Empty(_payer.Calls);
        Assert.Empty(_scheduler.Scheduled);
    }

    [Fact]
    public async Task Existing_pending_attempt_is_never_stacked_on()
    {
        var (payment, attempt) = Seed();
        _db.RetryAttempts.Add(new RetryAttempt
        {
            Id = Guid.NewGuid(),
            FailedPaymentId = payment.Id,
            AttemptNumber = 2,
            ScheduledFor = DateTime.UtcNow.AddDays(1),
        });
        _db.SaveChanges();
        _payer.NextResult = InvoicePayResult.Declined("card_declined", "declined");

        await Service().ExecuteAsync(attempt.Id);

        Assert.Equal(2, payment.RetryAttempts.Count);
        Assert.Equal(RecoveryStatus.ActiveRecovery, payment.Status);
        Assert.Empty(_scheduler.Scheduled);
    }

    [Fact]
    public async Task Unknown_attempt_id_is_a_noop()
    {
        await Service().ExecuteAsync(Guid.NewGuid());

        Assert.Empty(_payer.Calls);
        Assert.Empty(_scheduler.Scheduled);
    }
}
