using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Domain.Services;

namespace RecoverFlow.Application.Recovery;

public sealed class PaymentRecoveryService(
    IAppDbContext db,
    DunningEmailService dunningEmails,
    IRetryJobScheduler retryJobs,
    IOptions<RetryOptions> retryOptions,
    ILogger<PaymentRecoveryService> log)
{
    private readonly RetryOptions _retry = retryOptions.Value;

    public async Task HandlePaymentFailedAsync(PaymentFailedEvent e, CancellationToken ct = default)
    {
        var merchant = await db.Merchants
            .FirstOrDefaultAsync(m => m.StripeAccountId == e.StripeAccountId, ct);
        if (merchant is null)
        {
            log.LogWarning("Ignoring invoice.payment_failed for unknown Stripe account {Account}", e.StripeAccountId);
            return;
        }

        var payment = await db.FailedPayments
            .Include(p => p.RetryAttempts)
            .FirstOrDefaultAsync(p => p.MerchantId == merchant.Id && p.StripeInvoiceId == e.InvoiceId, ct);

        var isNewCase = payment is null;
        if (payment is null)
        {
            payment = new FailedPayment
            {
                Id = Guid.NewGuid(),
                MerchantId = merchant.Id,
                StripeInvoiceId = e.InvoiceId,
                StripeSubscriptionId = e.SubscriptionId,
                StripeCustomerId = e.CustomerId,
                CustomerEmail = e.CustomerEmail,
                AmountCents = e.AmountCents,
                Currency = e.Currency,
                Status = RecoveryStatus.ActiveRecovery,
                RecoveryMethod = RecoveryMethod.Unknown,
                FirstFailedAt = e.FailedAt,
            };
            db.FailedPayments.Add(payment);
        }

        payment.DeclineCode = e.DeclineCode;
        payment.DeclineReason = e.DeclineReason;
        payment.FailureType = DeclineCodeClassifier.Classify(e.DeclineCode);

        var attempt = ScheduleNextRetry(payment, e.FailedAt);
        await db.SaveChangesAsync(ct);

        if (isNewCase)
            await dunningEmails.SendStepAsync(payment.Id, 1, ct);

        // After the save, so the job can never fire against a row that doesn't exist yet.
        if (attempt is not null)
            retryJobs.ScheduleAttempt(attempt.Id, attempt.ScheduledFor);

        log.LogInformation(
            "Recovery {PaymentId} for invoice {InvoiceId}: {Amount} {Currency}, decline {DeclineCode} ({FailureType})",
            payment.Id, e.InvoiceId, e.AmountCents, e.Currency, e.DeclineCode, payment.FailureType);
    }

    public async Task HandleInvoicePaidAsync(InvoicePaidEvent e, CancellationToken ct = default)
    {
        var payment = await db.FailedPayments
            .Include(p => p.RetryAttempts)
            .Include(p => p.EmailEntries)
            .FirstOrDefaultAsync(p =>
                p.Merchant.StripeAccountId == e.StripeAccountId
                && p.StripeInvoiceId == e.InvoiceId
                && p.Status == RecoveryStatus.ActiveRecovery, ct);
        if (payment is null) return; // Not an invoice we were recovering

        payment.Status = RecoveryStatus.Recovered;
        payment.RecoveredAt = e.PaidAt;
        payment.StripeChargeId = e.ChargeId;
        payment.RecoveryMethod = await AttributeRecoveryAsync(payment, ct);

        if (e.ChargeId is null)
            log.LogWarning(
                "Invoice {InvoiceId} paid with no charge id on the event; a later refund or dispute " +
                "of this recovery cannot be matched back to it automatically",
                e.InvoiceId);

        // Cancel anything still pending for this invoice
        foreach (var a in payment.RetryAttempts.Where(a => a.AttemptedAt is null && a.Result is null))
            a.Result = "skipped";

        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Recovered invoice {InvoiceId} ({Amount} {Currency}) via {Method}",
            e.InvoiceId, payment.AmountCents, payment.Currency, payment.RecoveryMethod);
    }

    /// <summary>
    /// A recovery handed back to the customer: refunded, or lost to a chargeback. Recording it is
    /// what stops us billing for revenue the merchant no longer has — billing reads these fields to
    /// drop the case from an unsent invoice, or to credit the fee back if it was already charged.
    /// </summary>
    public async Task HandleRecoveryReversedAsync(RecoveryReversedEvent e, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(e.ChargeId)) return;

        var payment = await db.FailedPayments
            .FirstOrDefaultAsync(p =>
                p.Merchant.StripeAccountId == e.StripeAccountId
                && p.StripeChargeId == e.ChargeId
                && p.Status == RecoveryStatus.Recovered, ct);
        if (payment is null) return; // Not a recovery of ours, so nothing was ever billed for it

        // Stripe reports refunds as a running total and a dispute can follow a partial refund, so
        // take the larger figure rather than adding: adding would double-count a redelivered event.
        var reversed = Math.Min(payment.AmountCents, Math.Max(payment.ReversedAmountCents, e.ReversedAmountCents));
        if (payment.ReversedAtUtc is not null && reversed <= payment.ReversedAmountCents) return;

        payment.ReversedAmountCents = reversed;
        payment.ReversalReason = e.Reason;
        payment.ReversedAtUtc ??= e.ReversedAt;
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Recovery {PaymentId} on invoice {InvoiceId} reversed by {Reason}: {Reversed} of {Amount} {Currency}",
            payment.Id, payment.StripeInvoiceId, e.Reason, reversed, payment.AmountCents, payment.Currency);
    }

    private RetryAttempt? ScheduleNextRetry(FailedPayment payment, DateTime failedAt)
    {
        if (!DeclineCodeClassifier.ShouldRetry(payment.DeclineCode))
        {
            log.LogInformation("Hard decline {DeclineCode} on invoice {InvoiceId}, no retry scheduled", payment.DeclineCode, payment.StripeInvoiceId);
            return null;
        }

        // A pending attempt already covers this invoice (e.g. our own failed retry
        // re-triggered invoice.payment_failed) — never stack a second one.
        if (payment.RetryAttempts.Any(a => a.AttemptedAt is null && a.Result is null))
            return null;

        var attemptNumber = payment.RetryAttempts.Count + 1;
        if (attemptNumber > _retry.MaxRetryAttempts) return null;

        var at = RetryScheduler.CalculateNextRetry(payment.DeclineCode, attemptNumber, failedAt);
        if (at == DateTime.MaxValue) return null; // Card-update path or schedule exhausted

        var attempt = new RetryAttempt
        {
            Id = Guid.NewGuid(),
            FailedPaymentId = payment.Id,
            AttemptNumber = attemptNumber,
            ScheduledFor = at,
        };
        // Via the DbSet: added through the tracked payment's navigation, EF would
        // take the set Guid key to mean an existing row and issue an UPDATE.
        db.RetryAttempts.Add(attempt);
        return attempt;
    }

    // Attribution per spec: our retry succeeded > card update completed > email nudged them.
    private async Task<RecoveryMethod> AttributeRecoveryAsync(FailedPayment payment, CancellationToken ct)
    {
        if (payment.RetryAttempts.Any(a => a.Result == "success" || a.AttemptedAt is not null))
            return RecoveryMethod.SmartRetry;

        var cardUpdated = await db.CardUpdateSessions
            .AnyAsync(s => s.FailedPaymentId == payment.Id && s.CompletedAt != null, ct);
        if (cardUpdated) return RecoveryMethod.CardUpdate;

        if (payment.EmailEntries.Count > 0) return RecoveryMethod.EmailSequence;

        return RecoveryMethod.Unknown; // Paid without our intervention — no performance fee
    }
}
