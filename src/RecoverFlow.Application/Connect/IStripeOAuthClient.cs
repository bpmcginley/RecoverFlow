namespace RecoverFlow.Application.Connect;

/// <summary>
/// Raised when Stripe refuses an OAuth token exchange, or answers with something we cannot read.
/// Separate from Stripe.net's StripeException because the OAuth endpoints return a different
/// error shape from the rest of the API, and because a failed exchange is a dead end for that
/// install: the code is one-time use, so there is nothing to retry.
/// </summary>
public sealed class StripeOAuthException(
    string message, int? statusCode = null, string? error = null, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>The HTTP status Stripe answered with, or null if it never answered readably.</summary>
    public int? StatusCode { get; } = statusCode;

    /// <summary>Stripe's machine-readable <c>error</c> field, such as <c>invalid_grant</c>.</summary>
    public string? Error { get; } = error;

    /// <summary>
    /// True when Stripe refused the grant itself rather than failing to answer. Only a refusal
    /// says the authorization is gone; a 500 or a timeout says Stripe is having a bad minute.
    /// Treating the two alike would mark every merchant disconnected during one outage.
    /// </summary>
    public bool IsGrantRejected => StatusCode is >= 400 and < 500;
}

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
