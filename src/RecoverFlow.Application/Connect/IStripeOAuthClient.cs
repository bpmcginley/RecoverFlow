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

public interface IStripeOAuthClient
{
    Task<StripeOAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Trades a refresh token for a fresh access token. The result carries a NEW refresh token
    /// that replaces the one passed in; persist it or the next refresh has nothing to spend.
    /// </summary>
    Task<StripeOAuthTokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default);
}
