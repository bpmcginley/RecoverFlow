namespace RecoverFlow.Application.Common;

public sealed class StripeOptions
{
    public const string Section = "Stripe";

    public string SecretKey { get; set; } = "";
    public string ClientId { get; set; } = "";
    /// <summary>
    /// Signing secret of the Connect platform's webhook endpoint, which carries events from
    /// merchants who authorized through Connect OAuth and the platform's own fee invoices.
    /// </summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>
    /// Signing secret of the webhook endpoint on the account that owns the Stripe App
    /// (acct_1U3nACLGfYzeGCai). Stripe delivers events from accounts that installed the app —
    /// including the uninstall notice — to a "connected accounts" endpoint on <em>that</em>
    /// account, never to the Connect platform's, and each endpoint signs with its own secret.
    /// Both endpoints post to the same URL, so /webhooks/stripe checks a delivery against
    /// whichever of the two secrets are set. Leave blank if the app account has no endpoint.
    /// </summary>
    public string AppWebhookSecret { get; set; } = "";

    public string OAuthRedirectUri { get; set; } = "";

    /// <summary>Every webhook signing secret that is configured, in the order to try them.</summary>
    public IEnumerable<string> WebhookSecrets =>
        new[] { WebhookSecret, AppWebhookSecret }.Where(s => !string.IsNullOrEmpty(s));

    /// <summary>
    /// OAuth client ID of the published Stripe App, which is a different credential from the
    /// Connect <see cref="ClientId"/> and is issued on the app's own Settings tab. The merchant
    /// install flow uses this one; the free read-only audit still runs on Connect.
    /// </summary>
    public string AppClientId { get; set; } = "";

    /// <summary>
    /// Secret key of the account that owns the Stripe App, used to authenticate the app's OAuth
    /// token exchange and refresh. Stripe requires the <em>app developer's</em> key there, which
    /// is only the same string as <see cref="SecretKey"/> if the app happens to live on the same
    /// account as the rest of the integration. Leave it blank in that case and this falls back.
    /// Getting it wrong shows up as a permissions error on the exchange, not on startup.
    /// </summary>
    /// <remarks>
    /// Source: https://docs.stripe.com/stripe-apps/api-authentication/oauth, which authenticates
    /// both grants with <c>-u sk_live_***:</c> and says the error to expect is "you don't have
    /// the required permissions" when the wrong key is used.
    /// </remarks>
    public string AppSecretKey { get; set; } = "";

    /// <summary>
    /// Redirect URI for the read-only Marketplace audit app. Separate from
    /// <see cref="OAuthRedirectUri"/> on purpose: the audit grants read_only and keeps nothing,
    /// and the two flows must never be able to land on each other's callback.
    /// </summary>
    public string AuditRedirectUri { get; set; } = "";

    /// <summary>
    /// Signing secret (absec_...) of the embedded Stripe Dashboard app, used to verify the
    /// Stripe-Signature header on /app/v1 requests. Stripe only generates it on the app's
    /// first upload, so this ships EMPTY; while it is empty every app endpoint answers
    /// 503 app_not_configured rather than skipping verification.
    /// </summary>
    public string AppSigningSecret { get; set; } = "";
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

    /// <summary>
    /// Base64 verification key from SendGrid's Signed Event Webhook settings, used to verify
    /// signatures on /webhooks/sendgrid. Ships EMPTY; while it is empty the endpoint answers
    /// 503 rather than accepting unverified events.
    /// </summary>
    public string WebhookVerificationKey { get; set; } = "";
}

public sealed class EncryptionOptions
{
    public const string Section = "Encryption";

    public string Key { get; set; } = "";
}

public sealed class BillingOptions
{
    public const string Section = "Billing";

    /// <summary>Ships dark; flip Billing__Enabled=true in the environment to start billing.</summary>
    public bool Enabled { get; set; }
    public int FeeBasisPoints { get; set; } = 2500;
    public long MonthlyMinimumCents { get; set; } = 2900;
    /// <summary>
    /// Absolute ceiling on a single monthly invoice, published at /pricing/ and in the terms.
    /// Unlike the minimum it is never waived, and it is a one-way commitment: lowering it is
    /// fine, raising it is a price rise on everyone who signed up under the published number.
    /// </summary>
    public long MonthlyCapCents { get; set; } = 29_900;
    /// <summary>Days after signup during which the monthly minimum is waived (the % still applies).</summary>
    public int TrialDays { get; set; } = 30;
    public int InvoiceDueDays { get; set; } = 7;
    /// <summary>Stripe won't collect payments under $0.50; totals below this roll into next month.</summary>
    public long MinimumInvoiceableCents { get; set; } = 50;
}

public sealed class BacktestOptions
{
    public const string Section = "Backtest";

    /// <summary>How far back the connect-time scan looks over the merchant's Stripe history.</summary>
    public int WindowDays { get; set; } = 90;
    /// <summary>Caps the scan so a huge account can't stall the job or burn Stripe rate limits.</summary>
    public int MaxInvoices { get; set; } = 200;
}

public sealed class AdminOptions
{
    public const string Section = "Admin";

    /// <summary>
    /// Guards the internal signup dashboard. Ships EMPTY, which makes the whole admin
    /// surface return 404 rather than 401, so an unconfigured deployment does not even
    /// advertise that it exists. Set Admin__ApiKey in the environment to switch it on,
    /// and treat it like any other secret: these endpoints expose merchant email addresses.
    /// </summary>
    public string ApiKey { get; set; } = "";
}

public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>Public base URL of the API itself (api.recoverflow.org).</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Public base URL of the customer dashboard (app.recoverflow.org).
    /// Magic-link sign-in URLs are built against this so the session cookie lands
    /// on the app subdomain.</summary>
    public string DashboardUrl { get; set; } = "";

    public string CardUpdatePortalUrl { get; set; } = "";
}
