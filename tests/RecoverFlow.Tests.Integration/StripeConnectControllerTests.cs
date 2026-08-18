using System.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecoverFlow.Api.Controllers;
using RecoverFlow.Application.Backtest;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Connect;
using RecoverFlow.Infrastructure.Persistence;

namespace RecoverFlow.Tests.Integration;

/// <summary>
/// Guards the merchant install link. Stripe rejected RecoverFlow v0.0.2 because this route sent
/// merchants arriving from the Marketplace listing to a Connect authorize URL, which is not an
/// app install and renders "The provided OAuth link is invalid" — so the shape of this one URL
/// is the difference between a listed app and a rejected one.
/// </summary>
public class StripeConnectControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly StripeConnectController _controller;
    private readonly AppDbContext _db;
    private readonly EphemeralDataProtectionProvider _protection = new();

    public StripeConnectControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _controller = Build(new UnusedOAuth());
    }

    private StripeConnectController Build(IStripeOAuthClient oauth)
    {
        var stripe = Options.Create(new StripeOptions
        {
            ClientId = "ca_CONNECT_ONLY",
            AppClientId = "ca_app_TEST",
            OAuthRedirectUri = "https://api.recoverflow.org/connect/stripe/callback",
        });
        var tokens = new MerchantStripeTokenProvider(
            _db, new UnusedOAuth(), new UnusedEncryptor(),
            NullLogger<MerchantStripeTokenProvider>.Instance);

        return new StripeConnectController(
            stripe, _protection,
            new StripeConnectService(_db, oauth, new UnusedEncryptor()),
            new AccountBacktestService(
                _db, new UnusedInvoiceReader(), tokens,
                Options.Create(new BacktestOptions()), Options.Create(new BillingOptions()),
                NullLogger<AccountBacktestService>.Instance),
            new UnusedJobScheduler(), _db,
            NullLogger<StripeConnectController>.Instance);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Authorize_sends_merchants_to_the_stripe_apps_marketplace_not_connect()
    {
        var url = Assert.IsType<RedirectResult>(_controller.Authorize("a@b.com", "Acme")).Url;

        Assert.StartsWith("https://marketplace.stripe.com/oauth/v2/authorize", url);
        Assert.DoesNotContain("connect.stripe.com", url);
    }

    [Fact]
    public void Authorize_uses_the_app_client_id_not_the_connect_one()
    {
        var q = Query(_controller.Authorize("a@b.com", "Acme"));

        // Two different credentials live in StripeOptions. The Connect id still drives the free
        // read-only audit; sending it here is exactly the misconfiguration Stripe flagged.
        Assert.Equal("ca_app_TEST", q["client_id"]);
        Assert.Equal("https://api.recoverflow.org/connect/stripe/callback", q["redirect_uri"]);
    }

    [Fact]
    public void Authorize_asks_for_no_scope_because_the_manifest_grants_the_permissions()
    {
        var q = Query(_controller.Authorize("a@b.com", "Acme"));

        // Stripe Apps OAuth takes neither response_type nor scope: the consent screen is built
        // from stripe-app.json's permissions block. A scope here would be silently ignored, so
        // its presence would only mislead the next reader into thinking access is capped here.
        Assert.Null(q["scope"]);
        Assert.Null(q["response_type"]);
        Assert.False(string.IsNullOrEmpty(q["state"]));
    }

    [Fact]
    public void Authorize_rejects_a_request_with_no_email_or_company()
    {
        Assert.IsType<BadRequestObjectResult>(_controller.Authorize("", "Acme"));
        Assert.IsType<BadRequestObjectResult>(_controller.Authorize("a@b.com", " "));
    }

    /// <summary>
    /// App review's actual complaint about v0.0.3: the install succeeded on Stripe's side and
    /// then the callback answered HTTP 500. A failed exchange is still a failure, but it has to
    /// arrive as a handled status with something a merchant can act on, not as a stack trace.
    /// </summary>
    [Fact]
    public async Task A_failed_token_exchange_does_not_escape_the_callback_as_a_500()
    {
        var controller = Build(new RefusingOAuth());
        var state = Query(controller.Authorize("a@b.com", "Acme"))["state"]!;

        var result = await controller.Callback("ac_123", state, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
        Assert.NotEqual(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    private static System.Collections.Specialized.NameValueCollection Query(IActionResult r) =>
        HttpUtility.ParseQueryString(new Uri(Assert.IsType<RedirectResult>(r).Url).Query);

    // Authorize builds a URL and nothing else, so the collaborators the constructor needs are
    // never touched. They throw rather than return nulls, so a future test that does reach them
    // fails loudly instead of asserting against a fake that quietly does nothing.
    private sealed class UnusedOAuth : IStripeOAuthClient
    {
        public Task<StripeOAuthTokenResult> ExchangeAppInstallCodeAsync(string code, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<StripeOAuthTokenResult> ExchangeAuditCodeAsync(string code, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<StripeOAuthTokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Stands in for Stripe refusing the exchange, the way an expired ac_ code does.</summary>
    private sealed class RefusingOAuth : IStripeOAuthClient
    {
        public Task<StripeOAuthTokenResult> ExchangeAppInstallCodeAsync(string code, CancellationToken ct = default) =>
            throw new StripeOAuthException("Stripe rejected the app OAuth token exchange with 401.");
        public Task<StripeOAuthTokenResult> ExchangeAuditCodeAsync(string code, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<StripeOAuthTokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedEncryptor : ITokenEncryptor
    {
        public string Encrypt(string plaintext) => throw new NotSupportedException();
        public string Decrypt(string ciphertext) => throw new NotSupportedException();
    }

    private sealed class UnusedInvoiceReader : IHistoricalInvoiceReader
    {
        public Task<IReadOnlyList<HistoricalFailedInvoice>> ListUncollectedInvoicesAsync(
            string accessToken, DateTime sinceUtc, int maxInvoices, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedJobScheduler : IBacktestJobScheduler
    {
        public void Enqueue(Guid backtestId) => throw new NotSupportedException();
    }
}
