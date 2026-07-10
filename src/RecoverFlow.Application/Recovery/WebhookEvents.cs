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
    DateTime PaidAt);

/// <summary>Hangfire job contract: parse a verified Stripe event and route it.</summary>
public interface IStripeWebhookProcessor
{
    Task ProcessAsync(string eventJson);
}
