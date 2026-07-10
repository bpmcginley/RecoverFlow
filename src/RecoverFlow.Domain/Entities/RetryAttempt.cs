namespace RecoverFlow.Domain.Entities;

public class RetryAttempt
{
    public Guid Id { get; set; }
    public Guid FailedPaymentId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime ScheduledFor { get; set; }
    public DateTime? AttemptedAt { get; set; }
    public string? Result { get; set; } // success, failed, skipped
    public string? DeclineCodeReceived { get; set; }
    public FailedPayment FailedPayment { get; set; } = null!;
}
