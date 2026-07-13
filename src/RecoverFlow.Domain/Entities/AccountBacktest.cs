namespace RecoverFlow.Domain.Entities;

/// <summary>
/// Result of scanning a newly-connected merchant's recent Stripe history. The
/// "failed" figures are real (summed from their account); the "recoverable" figures
/// are an estimate range from our recovery model — never presented as a promise.
/// </summary>
public class AccountBacktest
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public BacktestStatus Status { get; set; }
    public int WindowDays { get; set; }

    // Real, straight from Stripe.
    public int FailedInvoiceCount { get; set; }
    public long FailedAmountCents { get; set; }
    public string Currency { get; set; } = "usd";

    // Estimated recoverable revenue (low/high) and our 25% fee on it.
    public long RecoverableLowCents { get; set; }
    public long RecoverableHighCents { get; set; }
    public long EstimatedFeeLowCents { get; set; }
    public long EstimatedFeeHighCents { get; set; }

    /// <summary>Per-decline-type breakdown, serialized (count + amount + recoverable range).</summary>
    public string BreakdownJson { get; set; } = "[]";

    public string? FailureReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public Merchant Merchant { get; set; } = null!;
}
