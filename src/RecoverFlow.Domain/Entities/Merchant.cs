namespace RecoverFlow.Domain.Entities;

public class Merchant
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string CompanyName { get; set; } = null!;
    public string StripeAccountId { get; set; } = null!;
    public string? EncryptedStripeAccessToken { get; set; }

    /// <summary>
    /// Exchanged for a fresh access token once the current one expires. Stripe issues a NEW
    /// refresh token on every exchange, so this column is rewritten each time, not just read.
    /// Null for merchants connected before the Stripe Apps migration.
    /// </summary>
    public string? EncryptedStripeRefreshToken { get; set; }

    /// <summary>
    /// When <see cref="EncryptedStripeAccessToken"/> stops working. Stripe Apps access tokens
    /// last an hour. NULL means "never expires" and marks a legacy Connect OAuth token, which
    /// genuinely did not expire — so a null here must not be read as "expired long ago".
    /// </summary>
    public DateTime? StripeAccessTokenExpiresAtUtc { get; set; }

    /// <summary>Customer on the platform's own Stripe account, used to invoice our fees. Created lazily on first bill.</summary>
    public string? StripePlatformCustomerId { get; set; }
    public string Plan { get; set; } = "free_trial"; // free_trial, starter, growth, scale
    public DateTime CreatedAt { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public ICollection<FailedPayment> FailedPayments { get; set; } = [];
}
