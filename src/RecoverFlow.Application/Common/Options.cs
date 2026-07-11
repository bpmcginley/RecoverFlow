namespace RecoverFlow.Application.Common;

public sealed class StripeOptions
{
    public const string Section = "Stripe";

    public string SecretKey { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string OAuthRedirectUri { get; set; } = "";
}

public sealed class RetryOptions
{
    public const string Section = "RetryConfig";

    public int MaxRetryAttempts { get; set; } = 4;
    public int MinHoursBetweenRetries { get; set; } = 4;
    public int MaxDaysToRecover { get; set; } = 14;
    public int DefaultSequenceSteps { get; set; } = 3;
}

public sealed class EmailOptions
{
    public const string Section = "Email";

    public string ApiKey { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "";
}

public sealed class EncryptionOptions
{
    public const string Section = "Encryption";

    public string Key { get; set; } = "";
}
