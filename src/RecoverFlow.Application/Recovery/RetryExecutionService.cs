using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Domain.Services;

namespace RecoverFlow.Application.Recovery;

/// <summary>
/// Hangfire job body: executes one scheduled retry attempt against Stripe.
/// Safe under at-least-once delivery — a finished attempt (Result set) is never
/// re-run, and the attempt id doubles as the Stripe idempotency key so a re-run
/// of a job that crashed mid-call cannot double-charge.
/// </summary>
public sealed class RetryExecutionService(
    IAppDbContext db,
    IStripeInvoicePayer stripe,
    IRetryJobScheduler retryJobs,
    IOptions<RetryOptions> retryOptions,
    ILogger<RetryExecutionService> log)
{
    private readonly RetryOptions _retry = retryOptions.Value;

    public async Task ExecuteAsync(Guid attemptId, CancellationToken ct = default)
    {
        var attempt = await db.RetryAttempts
            .Include(a => a.FailedPayment).ThenInclude(p => p.Merchant)
            .Include(a => a.FailedPayment).ThenInclude(p => p.RetryAttempts)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null)
        {
            log.LogWarning("Retry attempt {AttemptId} not found — nothing to execute", attemptId);
            return;
        }
        if (attempt.Result is not null) return; // Already executed, or marked skipped by invoice.paid

        var payment = attempt.FailedPayment;
        if (payment.Status != RecoveryStatus.ActiveRecovery)
        {
            attempt.Result = "skipped";
            await db.SaveChangesAsync(ct);
            return;
        }

        attempt.AttemptedAt = DateTime.UtcNow;
        var result = await stripe.PayInvoiceAsync(
            payment.StripeInvoiceId, payment.Merchant.StripeAccountId, attempt.Id.ToString(), ct);

        var next = result.Succeeded ? null : RecordFailure(payment, attempt, result);
        if (result.Succeeded)
        {
            // The invoice.paid webhook closes the case and attributes the recovery;
            // touching Status here would race it.
            attempt.Result = "success";
            log.LogInformation(
                "Retry {AttemptNumber} recovered invoice {InvoiceId} for merchant {MerchantId}",
                attempt.AttemptNumber, payment.StripeInvoiceId, payment.MerchantId);
        }

        await db.SaveChangesAsync(ct);

        // After the save, so the job can never fire against a row that doesn't exist yet.
        if (next is not null)
            retryJobs.ScheduleAttempt(next.Id, next.ScheduledFor);
    }

    private RetryAttempt? RecordFailure(FailedPayment payment, RetryAttempt attempt, InvoicePayResult result)
    {
        attempt.Result = "failed";
        attempt.DeclineCodeReceived = result.DeclineCode;
        log.LogInformation(
            "Retry {AttemptNumber} on invoice {InvoiceId} declined: {DeclineCode} ({Error})",
            attempt.AttemptNumber, payment.StripeInvoiceId, result.DeclineCode, result.Error);

        // The failed pay also emits invoice.payment_failed, whose handler may have
        // scheduled a follow-up already — never stack a second pending attempt.
        if (payment.RetryAttempts.Any(a => a.AttemptedAt is null && a.Result is null))
            return null;

        var next = NextAttempt(payment, attempt.AttemptNumber + 1, result.DeclineCode);
        if (next is not null)
        {
            // Via the DbSet: added through the tracked payment's navigation, EF would
            // take the set Guid key to mean an existing row and issue an UPDATE.
            db.RetryAttempts.Add(next);
        }
        else if (!DeclineCodeClassifier.RequiresCardUpdate(result.DeclineCode))
        {
            // Card-update declines keep the case open for the card-update flow;
            // anything else with no retry left is lost.
            payment.Status = RecoveryStatus.Lost;
            payment.LostAt = DateTime.UtcNow;
            log.LogInformation("Retries exhausted for invoice {InvoiceId} — marked lost", payment.StripeInvoiceId);
        }
        return next;
    }

    private RetryAttempt? NextAttempt(FailedPayment payment, int attemptNumber, string? declineCode)
    {
        if (!DeclineCodeClassifier.ShouldRetry(declineCode)) return null;
        if (attemptNumber > _retry.MaxRetryAttempts) return null;

        var at = RetryScheduler.CalculateNextRetry(declineCode, attemptNumber, DateTime.UtcNow);
        if (at == DateTime.MaxValue) return null;
        if (at > payment.FirstFailedAt.AddDays(_retry.MaxDaysToRecover)) return null;

        return new RetryAttempt
        {
            Id = Guid.NewGuid(),
            FailedPaymentId = payment.Id,
            AttemptNumber = attemptNumber,
            ScheduledFor = at,
        };
    }
}
