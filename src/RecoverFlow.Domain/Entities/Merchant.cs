namespace RecoverFlow.Domain.Entities;

public class Merchant
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string CompanyName { get; set; } = null!;
    public string StripeAccountId { get; set; } = null!;
    public string? EncryptedStripeAccessToken { get; set; }
    /// <summary>Customer on the platform's own Stripe account, used to invoice our fees. Created lazily on first bill.</summary>
    public string? StripePlatformCustomerId { get; set; }
    public string Plan { get; set; } = "free_trial"; // free_trial, starter, growth, scale
    public DateTime CreatedAt { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public ICollection<FailedPayment> FailedPayments { get; set; } = [];
}
