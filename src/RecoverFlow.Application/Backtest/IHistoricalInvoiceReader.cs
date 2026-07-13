namespace RecoverFlow.Application.Backtest;

/// <summary>One historical invoice that failed at least once and was never collected.</summary>
public sealed record HistoricalFailedInvoice(
    string InvoiceId,
    long AmountCents,
    string Currency,
    string? DeclineCode);

/// <summary>
/// Reads a connected merchant's recent lost/at-risk invoices from Stripe. SDK-free so the
/// application layer stays testable; the Stripe implementation lives in Infrastructure.
/// </summary>
public interface IHistoricalInvoiceReader
{
    /// <summary>
    /// Uncollected invoices (uncollectible, void, or past-due open) created since
    /// <paramref name="sinceUtc"/>, capped at <paramref name="maxInvoices"/> most-recent.
    /// </summary>
    Task<IReadOnlyList<HistoricalFailedInvoice>> ListUncollectedInvoicesAsync(
        string stripeAccountId, DateTime sinceUtc, int maxInvoices, CancellationToken ct = default);
}
