namespace RecoverFlow.Application.Recovery;

/// <summary>Outcome of an invoice pay attempt, mapped to plain data so the use case stays Stripe-SDK-free.</summary>
public sealed record InvoicePayResult(bool Succeeded, string? DeclineCode, string? Error)
{
    public static readonly InvoicePayResult Success = new(true, null, null);
    public static InvoicePayResult Declined(string? declineCode, string? error) => new(false, declineCode, error);
}

/// <summary>Pays an invoice on the merchant's own Stripe account, as the installed app.</summary>
public interface IStripeInvoicePayer
{
    /// <param name="accessToken">
    /// The merchant's Stripe Apps OAuth access token, from
    /// <see cref="Connect.MerchantStripeTokenProvider"/>. It is the credential itself, not an
    /// account id, so it must be live: these expire hourly.
    /// </param>
    Task<InvoicePayResult> PayInvoiceAsync(
        string invoiceId, string accessToken, string idempotencyKey, CancellationToken ct = default);
}
