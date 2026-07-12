namespace RecoverFlow.Domain.Entities;

/// <summary>
/// One billing run's fee invoice for a merchant: 25% of attributably-recovered revenue
/// plus a monthly-minimum top-up. Amounts are snapshotted here so a Failed invoice can be
/// re-sent later without recomputing (the underlying cases stay stamped with our Id).
/// </summary>
public class FeeInvoice
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    /// <summary>Run month, "yyyy-MM".</summary>
    public string PeriodLabel { get; set; } = null!;
    public long BillableRecoveredCents { get; set; }
    public int RecoveredCaseCount { get; set; }
    public long FeeCents { get; set; }
    /// <summary>Zero when the fee already meets the floor, or during the merchant's trial.</summary>
    public long FloorTopUpCents { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = "usd";
    public FeeInvoiceStatus Status { get; set; }
    public string? StripeInvoiceId { get; set; }
    public string? HostedInvoiceUrl { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public Merchant Merchant { get; set; } = null!;
}
