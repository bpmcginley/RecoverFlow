namespace RecoverFlow.Domain.Entities;

public class FailedPayment
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public string StripeInvoiceId { get; set; } = null!;
    public string? StripeSubscriptionId { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? CustomerEmail { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = null!;
    public string? DeclineCode { get; set; }
    public string? DeclineReason { get; set; }
    public DeclineType FailureType { get; set; }
    public RecoveryStatus Status { get; set; }
    public RecoveryMethod RecoveryMethod { get; set; }
    public DateTime FirstFailedAt { get; set; }
    public DateTime? RecoveredAt { get; set; }
    public DateTime? LostAt { get; set; }
    public Merchant Merchant { get; set; } = null!;
    public ICollection<RetryAttempt> RetryAttempts { get; set; } = [];
    public ICollection<EmailSequenceEntry> EmailEntries { get; set; } = [];
}
