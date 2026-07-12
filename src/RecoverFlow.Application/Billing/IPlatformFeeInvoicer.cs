namespace RecoverFlow.Application.Billing;

public sealed record PlatformCustomerResult(bool Succeeded, string? CustomerId, string? Error)
{
    public static PlatformCustomerResult Success(string customerId) => new(true, customerId, null);
    public static PlatformCustomerResult Failure(string error) => new(false, null, error);
}

public sealed record FeeInvoiceLine(long AmountCents, string Description);

/// <summary>StripeInvoiceId may be non-null even on failure — the invoice can exist at
/// Stripe when a later step (items, send) fails, and persisting it lets the next run resume.</summary>
public sealed record FeeInvoiceSendResult(bool Succeeded, string? StripeInvoiceId, string? HostedInvoiceUrl, string? Error);

/// <summary>
/// Bills merchants on the platform's own Stripe account (they connected via OAuth, so we
/// hold no card): hosted invoices Stripe emails with a payment link.
/// </summary>
public interface IPlatformFeeInvoicer
{
    Task<PlatformCustomerResult> EnsureCustomerAsync(
        Guid merchantId, string email, string companyName, CancellationToken ct = default);

    /// <summary>Idempotent per feeInvoiceId across runs; pass knownStripeInvoiceId when resuming.</summary>
    Task<FeeInvoiceSendResult> SendFeeInvoiceAsync(
        string customerId,
        Guid feeInvoiceId,
        IReadOnlyList<FeeInvoiceLine> lines,
        string currency,
        int daysUntilDue,
        string? knownStripeInvoiceId,
        CancellationToken ct = default);
}
