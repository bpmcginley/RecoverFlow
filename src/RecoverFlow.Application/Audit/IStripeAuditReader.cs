namespace RecoverFlow.Application.Audit;

/// <summary>Minimal identity read for the account that just installed the audit app.</summary>
public sealed record AuditAccount(string? Email, string? CompanyName);

/// <summary>
/// One failed charge, read read-only from a merchant's Stripe account. This is the whole
/// input to the retry-waste audit: the decline code says whether a retry could ever have
/// worked, and the card fingerprint + timestamp reconstruct the Visa reattempt budget.
/// </summary>
public sealed record FailedCharge(
    string? DeclineCode,
    long AmountCents,
    string Currency,
    DateTime CreatedUtc,
    string? CardKey);

/// <summary>
/// Read-only Stripe access for the "Retry Waste Audit" marketplace app. Every call reads;
/// none writes. The SDK-free surface keeps the audit computation testable in Application,
/// with the Stripe implementation in Infrastructure — the same split the backtest uses.
/// </summary>
public interface IStripeAuditReader
{
    /// <summary>The account's email and business name, for delivering and titling the report.</summary>
    Task<AuditAccount> GetAccountAsync(string stripeAccountId, CancellationToken ct = default);

    /// <summary>
    /// Failed charges created since <paramref name="sinceUtc"/>, capped at
    /// <paramref name="maxCharges"/> most-recent. Read via the platform key + Stripe-Account
    /// header, so no merchant access token is ever held or stored.
    /// </summary>
    Task<IReadOnlyList<FailedCharge>> ListFailedChargesAsync(
        string stripeAccountId, DateTime sinceUtc, int maxCharges, CancellationToken ct = default);
}
