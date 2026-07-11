using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;

namespace RecoverFlow.Application.Recovery;

/// <summary>
/// Sends the dunning email for a recovery case and records it on the case history.
/// The retry worker calls <see cref="SendStepAsync"/> with the matching schedule step.
/// </summary>
public sealed class DunningEmailService(
    IAppDbContext db,
    IEmailSender emailSender,
    ILogger<DunningEmailService> log)
{
    /// <summary>Idempotent per step; skips closed cases and cases without a customer email.</summary>
    public async Task SendStepAsync(Guid failedPaymentId, int sequenceStep, CancellationToken ct = default)
    {
        var payment = await db.FailedPayments
            .Include(p => p.Merchant)
            .Include(p => p.EmailEntries)
            .FirstOrDefaultAsync(p => p.Id == failedPaymentId, ct);

        if (payment is null || payment.Status != RecoveryStatus.ActiveRecovery) return;
        if (payment.EmailEntries.Any(e => e.SequenceStep == sequenceStep)) return;

        if (string.IsNullOrWhiteSpace(payment.CustomerEmail))
        {
            log.LogInformation("Case {PaymentId} has no customer email — skipping dunning step {Step}",
                payment.Id, sequenceStep);
            return;
        }

        var content = DunningEmailTemplates.ForStep(
            sequenceStep, payment.Merchant.CompanyName, payment.AmountCents, payment.Currency);
        await emailSender.SendAsync(payment.CustomerEmail, content.Subject, content.Html, ct);

        db.EmailSequences.Add(new EmailSequenceEntry
        {
            Id = Guid.NewGuid(),
            FailedPaymentId = payment.Id,
            SequenceStep = sequenceStep,
            EmailType = content.EmailType,
            SentAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        log.LogInformation("Sent {EmailType} (step {Step}) for case {PaymentId} to {Email}",
            content.EmailType, sequenceStep, payment.Id, payment.CustomerEmail);
    }
}
