using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Connect;
using Stripe;

namespace RecoverFlow.Infrastructure.Stripe;

/// <summary>
/// Exchanges a Standard Connect OAuth authorization code for the connected merchant's
/// access token. Uses the platform secret key (never the merchant's token) for the exchange.
/// </summary>
public sealed class StripeOAuthClient(IOptions<StripeOptions> stripeOptions) : IStripeOAuthClient
{
    public async Task<StripeOAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken ct = default)
    {
        var service = new OAuthTokenService();
        var token = await service.CreateAsync(
            new OAuthTokenCreateOptions
            {
                GrantType = "authorization_code",
                Code = code,
                ClientSecret = stripeOptions.Value.SecretKey,
            },
            cancellationToken: ct);

        // Stripe.net flags AccessToken/RefreshToken obsolete, recommending the platform key +
        // StripeUserId (Stripe-Account header) pattern instead. RecoverFlow's spec calls for
        // storing an encrypted merchant token, so we keep reading it deliberately for now.
#pragma warning disable CS0618
        return new StripeOAuthTokenResult(token.StripeUserId, token.AccessToken, token.RefreshToken, token.Scope);
#pragma warning restore CS0618
    }
}
