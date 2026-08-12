namespace RecoverFlow.Application.Recovery;

/// <summary>invoice.payment_failed, mapped to plain data so the use case stays Stripe-SDK-free.</summary>
public sealed record PaymentFailedEvent(
    string StripeAccountId,
    string InvoiceId,
    string? SubscriptionId,
    string? CustomerId,
    string? CustomerEmail,
    long AmountCents,
    string Currency,
    string? DeclineCode,
    string? DeclineReason,
    DateTime FailedAt);

/// <summary>invoice.paid — a potential recovery to attribute.</summary>
public sealed record InvoicePaidEvent(
    string StripeAccountId,
    string InvoiceId,
    string? ChargeId,
    DateTime PaidAt);

/// <summary>
/// A recovery taken back off the merchant: charge.refunded or charge.dispute.created.
/// <paramref name="ReversedAmountCents"/> is the running total reversed for the charge, not the
/// delta, because Stripe reports refunds cumulatively and a charge can be refunded more than once.
/// </summary>
public sealed record RecoveryReversedEvent(
    string StripeAccountId,
    string ChargeId,
    long ReversedAmountCents,
    string Reason,
    DateTime ReversedAt);

/// <summary>Hangfire job contract: parse a verified Stripe event and route it.</summary>
public interface IStripeWebhookProcessor
{
    Task ProcessAsync(string eventJson);
}
