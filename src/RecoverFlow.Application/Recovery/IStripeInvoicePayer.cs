namespace RecoverFlow.Application.Recovery;

/// <summary>Outcome of an invoice pay attempt, mapped to plain data so the use case stays Stripe-SDK-free.</summary>
public sealed record InvoicePayResult(bool Succeeded, string? DeclineCode, string? Error)
{
    public static readonly InvoicePayResult Success = new(true, null, null);
    public static InvoicePayResult Declined(string? declineCode, string? error) => new(false, declineCode, error);
}

/// <summary>Pays a merchant's invoice on their connected Stripe account.</summary>
public interface IStripeInvoicePayer
{
    Task<InvoicePayResult> PayInvoiceAsync(
        string invoiceId, string stripeAccountId, string idempotencyKey, CancellationToken ct = default);
}
