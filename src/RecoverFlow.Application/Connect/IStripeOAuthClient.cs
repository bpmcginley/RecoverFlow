namespace RecoverFlow.Application.Connect;

/// <summary>
/// Raised when Stripe refuses an OAuth token exchange, or answers with something we cannot read.
/// Separate from Stripe.net's StripeException because the OAuth endpoints return a different
/// error shape from the rest of the API, and because a failed exchange is a dead end for that
/// install: the code is one-time use, so there is nothing to retry.
/// </summary>
public sealed class StripeOAuthException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <param name="ExpiresAtUtc">
/// Null when Stripe returned no expiry, which is the Connect OAuth case: those tokens live
/// until the merchant revokes them. Stripe Apps tokens always carry one (an hour out).
/// </param>
public sealed record StripeOAuthTokenResult(
    string StripeAccountId,
    string AccessToken,
    string? RefreshToken,
    string? Scope,
    DateTime? ExpiresAtUtc);

/// <remarks>
/// The two exchange methods are not interchangeable, and not only because they authenticate with
/// different secret keys. They post to different hosts: the app install to api.stripe.com and the
/// Connect audit to connect.stripe.com. Sending an app's <c>ac_</c> code to the Connect endpoint
/// is what made app review v0.0.3 fail, so the flows are named rather than sharing one method
/// with a flag.
/// </remarks>
public interface IStripeOAuthClient
{
    /// <summary>Completes a Stripe App install. Authenticated with the app account's secret key.</summary>
    Task<StripeOAuthTokenResult> ExchangeAppInstallCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Completes the free read-only audit's Connect authorization, which is a different grant on a
    /// different account. Authenticated with the Connect platform's secret key.
    /// </summary>
    Task<StripeOAuthTokenResult> ExchangeAuditCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Trades a refresh token for a fresh access token. The result carries a NEW refresh token
    /// that replaces the one passed in; persist it or the next refresh has nothing to spend.
    /// </summary>
    Task<StripeOAuthTokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default);
}
