using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Connect;
using RecoverFlow.Infrastructure.Stripe;

namespace RecoverFlow.Tests.Unit;

/// <summary>
/// App review rejected v0.0.3 with "callback returns HTTP 500" because the Stripe Apps install
/// code was being exchanged against connect.stripe.com, which does not recognise <c>ac_</c>
/// codes. Nothing here asserted the endpoint, so nothing caught it. These tests do.
/// </summary>
public class StripeOAuthClientTests
{
    /// <summary>Captures the outgoing request and answers with a canned response.</summary>
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Form { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Form = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    // Shaped exactly like the sample in docs.stripe.com/stripe-apps/api-authentication/oauth.
    // Note what is absent: there is no expires_in field on a Stripe Apps token response.
    private const string InstallResponse = """
        {
          "access_token": "sk_live_merchant",
          "livemode": true,
          "refresh_token": "rt_first",
          "scope": "stripe_apps",
          "stripe_publishable_key": "pk_live_merchant",
          "stripe_user_id": "acct_merchant",
          "token_type": "bearer"
        }
        """;

    private static (StripeOAuthClient Client, FakeHandler Handler) Build(
        HttpStatusCode status = HttpStatusCode.OK, string body = InstallResponse)
    {
        var handler = new FakeHandler(status, body);
        var options = Options.Create(new StripeOptions { SecretKey = "sk_platform", AppSecretKey = "sk_app" });
        return (new StripeOAuthClient(options, new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task An_app_install_code_is_exchanged_against_the_apps_endpoint_not_connect()
    {
        var (client, handler) = Build();

        await client.ExchangeAppInstallCodeAsync("ac_123");

        Assert.Equal("https://api.stripe.com/v1/oauth/token", handler.Request!.RequestUri!.ToString());
        Assert.DoesNotContain("connect.stripe.com", handler.Request.RequestUri.ToString());
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
    }

    [Fact]
    public async Task An_app_install_sends_the_code_and_grant_type_and_nothing_else()
    {
        var (client, handler) = Build();

        await client.ExchangeAppInstallCodeAsync("ac_123");

        Assert.Contains("code=ac_123", handler.Form);
        Assert.Contains("grant_type=authorization_code", handler.Form);
        // Stripe authenticates this with the key, not a client_secret body field. Sending one
        // is what the Connect shape wanted and it is not part of the Apps request.
        Assert.DoesNotContain("client_secret", handler.Form);
    }

    [Fact]
    public async Task The_app_secret_key_authenticates_the_exchange_as_the_basic_username()
    {
        var (client, handler) = Build();

        await client.ExchangeAppInstallCodeAsync("ac_123");

        var auth = handler.Request!.Headers.Authorization;
        Assert.Equal("Basic", auth!.Scheme);
        Assert.Equal("sk_app:", Encoding.ASCII.GetString(Convert.FromBase64String(auth.Parameter!)));
    }

    [Fact]
    public async Task Without_an_app_secret_key_the_platform_key_is_used_instead()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, InstallResponse);
        var options = Options.Create(new StripeOptions { SecretKey = "sk_platform", AppSecretKey = "" });
        var client = new StripeOAuthClient(options, new HttpClient(handler));

        await client.ExchangeAppInstallCodeAsync("ac_123");

        var auth = handler.Request!.Headers.Authorization;
        Assert.Equal("sk_platform:", Encoding.ASCII.GetString(Convert.FromBase64String(auth!.Parameter!)));
    }

    /// <summary>
    /// The response carries no expires_in, but the tokens die after an hour regardless. A null
    /// expiry here would make MerchantStripeTokenProvider treat the token as permanent and never
    /// refresh it, so every job running more than an hour after the install would fail.
    /// </summary>
    [Fact]
    public async Task A_token_response_with_no_expires_in_still_expires_in_an_hour()
    {
        var (client, _) = Build();

        var result = await client.ExchangeAppInstallCodeAsync("ac_123");

        var expiry = Assert.IsType<DateTime>(result.ExpiresAtUtc);
        Assert.InRange(expiry, DateTime.UtcNow.AddMinutes(59), DateTime.UtcNow.AddMinutes(61));
    }

    [Fact]
    public async Task An_explicit_expires_in_is_preferred_over_the_assumed_hour()
    {
        var (client, _) = Build(body: """
            {"access_token": "sk_live_m", "stripe_user_id": "acct_m", "expires_in": 7200}
            """);

        var result = await client.ExchangeAppInstallCodeAsync("ac_123");

        Assert.InRange(result.ExpiresAtUtc!.Value,
            DateTime.UtcNow.AddMinutes(119), DateTime.UtcNow.AddMinutes(121));
    }

    [Fact]
    public async Task The_install_response_is_read_into_the_result()
    {
        var (client, _) = Build();

        var result = await client.ExchangeAppInstallCodeAsync("ac_123");

        Assert.Equal("acct_merchant", result.StripeAccountId);
        Assert.Equal("sk_live_merchant", result.AccessToken);
        Assert.Equal("rt_first", result.RefreshToken);
        Assert.Equal("stripe_apps", result.Scope);
    }

    [Fact]
    public async Task A_refresh_spends_the_refresh_token_against_the_same_apps_endpoint()
    {
        var (client, handler) = Build(body: """
            {"access_token": "sk_live_m", "refresh_token": "rt_second", "account_id": "acct_m"}
            """);

        var result = await client.RefreshAsync("rt_first");

        Assert.Equal("https://api.stripe.com/v1/oauth/token", handler.Request!.RequestUri!.ToString());
        Assert.Contains("grant_type=refresh_token", handler.Form);
        Assert.Contains("refresh_token=rt_first", handler.Form);
        // Stripe rolls the refresh token on every exchange; the new one has to come back out
        // or MerchantStripeTokenProvider persists nothing and the next refresh has no credit.
        Assert.Equal("rt_second", result.RefreshToken);
    }

    /// <summary>Stripe documents the refresh response with account_id rather than stripe_user_id.</summary>
    [Fact]
    public async Task Either_account_id_or_stripe_user_id_names_the_account()
    {
        var (client, _) = Build(body: """
            {"access_token": "sk_live_m", "account_id": "acct_from_refresh"}
            """);

        var result = await client.RefreshAsync("rt_first");

        Assert.Equal("acct_from_refresh", result.StripeAccountId);
    }

    [Fact]
    public async Task A_rejected_exchange_raises_StripeOAuthException_carrying_Stripes_reason()
    {
        var (client, _) = Build(HttpStatusCode.Unauthorized, """
            {"error": "invalid_grant", "error_description": "Authorization code has expired."}
            """);

        var e = await Assert.ThrowsAsync<StripeOAuthException>(
            () => client.ExchangeAppInstallCodeAsync("ac_expired"));

        Assert.Contains("401", e.Message);
        Assert.Contains("invalid_grant", e.Message);
        Assert.Contains("Authorization code has expired.", e.Message);
    }

    [Fact]
    public async Task A_success_that_is_not_json_raises_StripeOAuthException_rather_than_escaping()
    {
        var (client, _) = Build(body: "<html>gateway</html>");

        await Assert.ThrowsAsync<StripeOAuthException>(
            () => client.ExchangeAppInstallCodeAsync("ac_123"));
    }

    [Fact]
    public async Task A_response_with_no_access_token_raises_StripeOAuthException()
    {
        var (client, _) = Build(body: """{"stripe_user_id": "acct_m"}""");

        await Assert.ThrowsAsync<StripeOAuthException>(
            () => client.ExchangeAppInstallCodeAsync("ac_123"));
    }
}
