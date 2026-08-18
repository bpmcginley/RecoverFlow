using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Connect;
using Stripe;

namespace RecoverFlow.Infrastructure.Stripe;

/// <summary>
/// Exchanges OAuth codes and refresh tokens for a merchant's access token, using one of our own
/// secret keys (never the merchant's own token) to authenticate the exchange.
/// </summary>
/// <remarks>
/// The two flows post to <b>different hosts</b>, which is the whole reason this class hand-rolls
/// one of them:
/// <list type="bullet">
/// <item>Stripe Apps installs go to <c>https://api.stripe.com/v1/oauth/token</c>.</item>
/// <item>Connect authorizations go to <c>https://connect.stripe.com/oauth/token</c>.</item>
/// </list>
/// Stripe.net's <see cref="OAuthTokenService"/> only knows the Connect one — the assembly has no
/// <c>/v1/oauth/token</c> string in it at all. Routing the app install through it sent the
/// <c>ac_</c> code to Connect, which does not recognise app codes, and app review v0.0.3 came
/// back with "callback returns HTTP 500". So the app install and its refresh are plain HTTP here
/// and only the Connect audit still uses the SDK.
///
/// Source: https://docs.stripe.com/stripe-apps/api-authentication/oauth
/// </remarks>
public sealed class StripeOAuthClient : IStripeOAuthClient
{
    internal const string AppTokenUrl = "https://api.stripe.com/v1/oauth/token";

    // Stripe documents Apps access tokens as lasting an hour but does not return expires_in on
    // the token response, so there is nothing to read. Assuming "no expiry stated" means "never
    // expires" is what Connect does, and it would leave MerchantStripeTokenProvider never
    // refreshing: every job more than an hour after the install would fail on a dead token.
    private static readonly TimeSpan AppTokenLifetime = TimeSpan.FromHours(1);

    // One host, one long-lived client: this is a handful of calls a day and Stripe.net keeps a
    // static client of its own for the same reason.
    private static readonly HttpClient Shared = new();

    private readonly IOptions<StripeOptions> stripeOptions;
    private readonly HttpClient http;

    /// <param name="http">
    /// Tests pass a fake handler here. Nothing in production does, so that the shared client
    /// and its connection pool are not rebuilt per scope.
    /// </param>
    public StripeOAuthClient(IOptions<StripeOptions> stripeOptions, HttpClient? http = null)
    {
        this.stripeOptions = stripeOptions;
        this.http = http ?? Shared;
    }

    // Stripe authenticates both app grants with the app developer's own secret key. That is a
    // different account from the Connect platform, so falling back to the platform key here is
    // only correct when the app happens to live on it. See StripeOptions.AppSecretKey.
    private string AppKey => string.IsNullOrWhiteSpace(stripeOptions.Value.AppSecretKey)
        ? stripeOptions.Value.SecretKey
        : stripeOptions.Value.AppSecretKey;

    public Task<StripeOAuthTokenResult> ExchangeAppInstallCodeAsync(string code, CancellationToken ct = default) =>
        PostAppTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
        }, ct);

    public Task<StripeOAuthTokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        PostAppTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, ct);

    /// <summary>
    /// The free read-only audit is genuinely Connect, so it stays on the SDK and on
    /// connect.stripe.com. Connect tokens carry no expiry, hence the null.
    /// </summary>
    public async Task<StripeOAuthTokenResult> ExchangeAuditCodeAsync(string code, CancellationToken ct = default)
    {
        var token = await new OAuthTokenService().CreateAsync(new OAuthTokenCreateOptions
        {
            GrantType = "authorization_code",
            Code = code,
            ClientSecret = stripeOptions.Value.SecretKey,
        }, cancellationToken: ct);

        // Stripe.net flags AccessToken/RefreshToken obsolete, recommending the platform key +
        // StripeUserId (Stripe-Account header) pattern instead. The audit reads the merchant's
        // own account directly, so it wants the token.
#pragma warning disable CS0618
        return new StripeOAuthTokenResult(
            token.StripeUserId, token.AccessToken, token.RefreshToken, token.Scope, null);
#pragma warning restore CS0618
    }

    private async Task<StripeOAuthTokenResult> PostAppTokenAsync(
        Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AppTokenUrl)
        {
            Content = new FormUrlEncodedContent(form),
        };

        // Stripe takes the secret key as the HTTP basic username with an empty password
        // (the "-u sk_live_***:" in the docs), not as a client_secret form field.
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(AppKey + ":")));

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new StripeOAuthException(DescribeFailure(response, body));

        JsonElement json;
        try
        {
            json = JsonDocument.Parse(body).RootElement;
        }
        catch (JsonException e)
        {
            throw new StripeOAuthException(
                $"Stripe returned {(int)response.StatusCode} with a body that is not JSON.", e);
        }

        var accessToken = Text(json, "access_token")
            ?? throw new StripeOAuthException("Stripe's token response carried no access_token.");

        // authorization_code returns stripe_user_id; the refresh response is documented with
        // account_id. Same value, so read either rather than betting on one.
        var accountId = Text(json, "stripe_user_id") ?? Text(json, "account_id")
            ?? throw new StripeOAuthException("Stripe's token response named no account.");

        return new StripeOAuthTokenResult(
            accountId,
            accessToken,
            Text(json, "refresh_token"),
            Text(json, "scope"),
            ExpiresAt(json));
    }

    private static string? Text(JsonElement json, string property) =>
        json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTime ExpiresAt(JsonElement json)
    {
        var lifetime = json.TryGetProperty("expires_in", out var expiresIn)
            && expiresIn.TryGetInt64(out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : AppTokenLifetime;

        return DateTime.UtcNow.Add(lifetime);
    }

    /// <summary>
    /// Stripe's OAuth errors are <c>{"error": "...", "error_description": "..."}</c> rather than
    /// the <c>{"error": {...}}</c> shape the rest of the API uses, so Stripe.net cannot parse
    /// them. The description is the part worth logging: it names the wrong-key case explicitly.
    /// </summary>
    private static string DescribeFailure(HttpResponseMessage response, string body)
    {
        var prefix = $"Stripe rejected the app OAuth token exchange with {(int)response.StatusCode}";
        try
        {
            var json = JsonDocument.Parse(body).RootElement;
            var error = Text(json, "error");
            var description = Text(json, "error_description");
            if (error is null && description is null) return $"{prefix}.";
            return $"{prefix}: {error ?? "error"} - {description ?? "no description"}";
        }
        catch (JsonException)
        {
            return $"{prefix}.";
        }
    }
}
