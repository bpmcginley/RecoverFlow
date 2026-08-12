using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Recovery;
using RecoverFlow.Domain.Entities;
using Stripe;

namespace RecoverFlow.Infrastructure.Stripe;

/// <summary>
/// Runs as a Hangfire job after the webhook controller has verified the signature.
/// Parses the event, dedupes by event id, and routes to the recovery use cases.
/// </summary>
public sealed class StripeWebhookProcessor(
    IAppDbContext db,
    PaymentRecoveryService recovery,
    ILogger<StripeWebhookProcessor> log) : IStripeWebhookProcessor
{
    public async Task ProcessAsync(string eventJson)
    {
        // Signature was already verified at the endpoint; this re-parse is trusted input.
        var stripeEvent = EventUtility.ParseEvent(eventJson, throwOnApiVersionMismatch: false);

        if (await db.ProcessedWebhookEvents.AnyAsync(w => w.EventId == stripeEvent.Id))
        {
            log.LogDebug("Skipping duplicate webhook event {EventId}", stripeEvent.Id);
            return;
        }

        switch (stripeEvent.Type)
        {
            case EventTypes.InvoicePaymentFailed when stripeEvent.Data.Object is Invoice invoice:
                await recovery.HandlePaymentFailedAsync(MapPaymentFailed(stripeEvent, invoice));
                break;

            case EventTypes.InvoicePaid when stripeEvent.Data.Object is Invoice invoice:
                await recovery.HandleInvoicePaidAsync(new InvoicePaidEvent(
                    stripeEvent.Account,
                    invoice.Id,
                    SettlingChargeId(invoice),
                    invoice.StatusTransitions?.PaidAt ?? stripeEvent.Created));
                break;

            // A recovery we billed for can be handed back later. Both events name a charge and
            // nothing else, which is why the paid invoice's charge id is stored on the case.
            case EventTypes.ChargeRefunded when stripeEvent.Data.Object is Charge charge:
                await recovery.HandleRecoveryReversedAsync(new RecoveryReversedEvent(
                    stripeEvent.Account, charge.Id, charge.AmountRefunded, "refund", stripeEvent.Created));
                break;

            case EventTypes.ChargeDisputeCreated when stripeEvent.Data.Object is Dispute dispute:
                await recovery.HandleRecoveryReversedAsync(new RecoveryReversedEvent(
                    stripeEvent.Account, dispute.ChargeId, dispute.Amount, "dispute", stripeEvent.Created));
                break;

            default:
                log.LogDebug("Unhandled webhook event type {EventType}", stripeEvent.Type);
                break;
        }

        db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
        {
            EventId = stripeEvent.Id,
            EventType = stripeEvent.Type,
            ProcessedAt = DateTime.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Lost the idempotency race to a concurrent delivery of the same event.
            log.LogInformation("Webhook event {EventId} was processed concurrently", stripeEvent.Id);
        }
    }

    // Modern Stripe invoices no longer carry a single charge id; they carry a list of payments.
    // Null is a legitimate answer (a payment recorded outside a card charge), and the caller
    // logs it — a case with no charge id simply can't be matched to a later refund.
    private static string? SettlingChargeId(Invoice invoice) =>
        invoice.Payments?.Data?
            .Select(p => p.Payment?.ChargeId)
            .LastOrDefault(id => !string.IsNullOrEmpty(id));

    private static PaymentFailedEvent MapPaymentFailed(Event stripeEvent, Invoice invoice)
    {
        var error = invoice.LastFinalizationError;
        return new PaymentFailedEvent(
            stripeEvent.Account,
            invoice.Id,
            invoice.Parent?.SubscriptionDetails?.SubscriptionId,
            invoice.CustomerId,
            invoice.CustomerEmail,
            invoice.AmountDue,
            invoice.Currency,
            error?.DeclineCode ?? error?.Code,
            error?.Message,
            stripeEvent.Created);
    }
}
