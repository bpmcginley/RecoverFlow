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
    /// <summary>
    /// The charge that settled this invoice, captured when we saw it get paid. Refund and
    /// dispute webhooks identify a charge and nothing else, so without this a reversal
    /// cannot be tied back to the case it undoes.
    /// </summary>
    public string? StripeChargeId { get; set; }
    /// <summary>Reserved by a billing run; set atomically with the FeeInvoice row, before Stripe is called.</summary>
    public Guid? FeeInvoiceId { get; set; }
    /// <summary>Set only once the fee invoice was successfully sent.</summary>
    public DateTime? BilledAtUtc { get; set; }
    /// <summary>The part of <see cref="AmountCents"/> that actually went onto an invoice, after
    /// anything already reversed at that moment was taken off. Zero until billed.</summary>
    public long BilledBaseCents { get; set; }
    /// <summary>This case's share of the fee its invoice actually charged. Stamped at send time
    /// so it already reflects the monthly cap, which means a credit can never exceed what we took.</summary>
    public long BilledFeeCents { get; set; }
    /// <summary>First time this recovery was taken back off the merchant, refunded or charged back.</summary>
    public DateTime? ReversedAtUtc { get; set; }
    /// <summary>"refund" or "dispute". A string so a new Stripe reason needs no migration.</summary>
    public string? ReversalReason { get; set; }
    /// <summary>Running total taken back, in the same units as <see cref="AmountCents"/>. Cumulative,
    /// not per-event: Stripe reports refunds as a running total, and a case can be refunded twice.</summary>
    public long ReversedAmountCents { get; set; }
    /// <summary>Fee already credited back for this case, so a reversal is never credited twice
    /// and a credit too large for one month can finish on the next.</summary>
    public long ReversalCreditedCents { get; set; }
    public Merchant Merchant { get; set; } = null!;
    public ICollection<RetryAttempt> RetryAttempts { get; set; } = [];
    public ICollection<EmailSequenceEntry> EmailEntries { get; set; } = [];
}
