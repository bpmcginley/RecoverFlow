namespace RecoverFlow.Application.Connect;

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
/// The two exchange methods hit the same Stripe endpoint but are not interchangeable: they
/// authenticate with different secret keys, because the Stripe App and the Connect platform are
/// separate accounts. Calling the wrong one fails at Stripe with a permissions error, so the
/// flows are named rather than sharing one method with a flag.
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
