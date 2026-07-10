namespace RecoverFlow.Domain.Entities;

// Idempotency ledger: Stripe may deliver the same event more than once.
public class ProcessedWebhookEvent
{
    public string EventId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public DateTime ProcessedAt { get; set; }
}
