using System.Text.Json;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Connect;
using Stripe;

namespace RecoverFlow.Infrastructure.Stripe;

/// <summary>
/// Exchanges OAuth codes and refresh tokens for a merchant's access token, using one of our own
/// secret keys (never the merchant's own token) to authenticate the exchange. The same
/// POST /v1/oauth/token endpoint serves both the Stripe Apps install and the read-only Connect
/// audit, but they are authenticated with different keys because they live on different accounts.
/// </summary>
public sealed class StripeOAuthClient(IOptions<StripeOptions> stripeOptions) : IStripeOAuthClient
{
    // Stripe authenticates both app grants with the app developer's own secret key. That is a
    // different account from the Connect platform, so falling back to the platform key here is
    // only correct when the app happens to live on it. See StripeOptions.AppSecretKey.
    private string AppKey => string.IsNullOrWhiteSpace(stripeOptions.Value.AppSecretKey)
        ? stripeOptions.Value.SecretKey
        : stripeOptions.Value.AppSecretKey;

    public Task<StripeOAuthTokenResult> ExchangeAppInstallCodeAsync(string code, CancellationToken ct = default) =>
        PostAsync(new OAuthTokenCreateOptions
        {
            GrantType = "authorization_code",
            Code = code,
            ClientSecret = AppKey,
        }, ct);

    public Task<StripeOAuthTokenResult> ExchangeAuditCodeAsync(string code, CancellationToken ct = default) =>
        PostAsync(new OAuthTokenCreateOptions
        {
            GrantType = "authorization_code",
            Code = code,
            ClientSecret = stripeOptions.Value.SecretKey,
        }, ct);

    public Task<StripeOAuthTokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        PostAsync(new OAuthTokenCreateOptions
        {
            GrantType = "refresh_token",
            RefreshToken = refreshToken,
            ClientSecret = AppKey,
        }, ct);

    private static async Task<StripeOAuthTokenResult> PostAsync(OAuthTokenCreateOptions options, CancellationToken ct)
    {
        var token = await new OAuthTokenService().CreateAsync(options, cancellationToken: ct);

        // Stripe.net flags AccessToken/RefreshToken obsolete, recommending the platform key +
        // StripeUserId (Stripe-Account header) pattern instead. That advice is Connect-only:
        // under Stripe Apps OAuth there is no connected account to name in a header, and the
        // merchant's token IS the credential. So we read them deliberately.
#pragma warning disable CS0618
        return new StripeOAuthTokenResult(
            token.StripeUserId, token.AccessToken, token.RefreshToken, token.Scope, ExpiresAt(token));
#pragma warning restore CS0618
    }

    // Stripe.net models this endpoint for Connect, whose tokens never expire, so it has no
    // ExpiresIn property at all — but Stripe Apps returns expires_in on the same response.
    // Reading it off the raw JSON is the only way to get it without hand-rolling the call.
    private static DateTime? ExpiresAt(OAuthToken token)
    {
        if (token.RawJsonElement is not { ValueKind: JsonValueKind.Object } raw) return null;
        if (!raw.TryGetProperty("expires_in", out var expiresIn)) return null;
        return expiresIn.TryGetInt64(out var seconds) && seconds > 0
            ? DateTime.UtcNow.AddSeconds(seconds)
            : null;
    }
}
