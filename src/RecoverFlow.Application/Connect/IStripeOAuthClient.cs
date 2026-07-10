namespace RecoverFlow.Application.Connect;

public sealed record StripeOAuthTokenResult(string StripeAccountId, string AccessToken, string? RefreshToken, string? Scope);

public interface IStripeOAuthClient
{
    Task<StripeOAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken ct = default);
}
