namespace RecoverFlow.Domain.Entities;

public class CardUpdateSession
{
    public Guid Id { get; set; }
    public Guid FailedPaymentId { get; set; }
    public string Token { get; set; } = null!; // Unique URL-safe token
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? NewPaymentMethodId { get; set; }
    public FailedPayment FailedPayment { get; set; } = null!;
}
