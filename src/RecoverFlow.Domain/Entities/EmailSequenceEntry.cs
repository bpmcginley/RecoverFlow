namespace RecoverFlow.Domain.Entities;

public class EmailSequenceEntry
{
    public Guid Id { get; set; }
    public Guid FailedPaymentId { get; set; }
    public int SequenceStep { get; set; } // 1, 2, 3
    public string EmailType { get; set; } = null!; // friendly_reminder, urgent, final_notice
    public DateTime SentAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? ClickedAt { get; set; }
    public bool ResultedInRecovery { get; set; }
    public FailedPayment FailedPayment { get; set; } = null!;
}
