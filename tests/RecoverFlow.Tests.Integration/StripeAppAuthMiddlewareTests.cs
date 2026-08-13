using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecoverFlow.Api.Middleware;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;
using Stripe;

namespace RecoverFlow.Tests.Integration;

/// <summary>
/// Exercises the /app/v1 auth gate: Stripe-Signature verification against the app signing
/// secret, acct_ to Merchant resolution, tenant scoping, and the null-origin CORS
/// handling the embedded Dashboard extension depends on.
/// </summary>
public sealed class StripeAppAuthMiddlewareTests : IDisposable
{
    private const string Secret = "absec_test_secret";
    private const string AccountId = "acct_app_test_1";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly Guid _merchantId = Guid.NewGuid();

    public StripeAppAuthMiddlewareTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        db.Merchants.Add(new Merchant
        {
            Id = _merchantId,
            Email = "merchant@example.com",
            CompanyName = "Test Co",
            StripeAccountId = AccountId,
            CreatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private static string Body(string accountId = AccountId, string userId = "usr_test_1") =>
        $"{{\"user_id\":\"{userId}\",\"account_id\":\"{accountId}\"}}";

    /// <summary>Builds a t=/v1= header the way fetchStripeSignature would, over the exact raw bytes.</summary>
    private static string Sign(string payload, string secret = Secret, long? timestamp = null)
    {
        var ts = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString();
        return $"t={ts},v1={EventUtility.ComputeSignature(secret, ts, payload)}";
    }

    private async Task<(HttpContext Http, TenantContext Tenant, bool NextCalled)> RunAsync(
        string method = "POST", string body = "", string? signature = null, string appSecret = Secret)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = "/app/v1/summary";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        http.Response.Body = new MemoryStream();
        if (signature is not null) http.Request.Headers["Stripe-Signature"] = signature;

        var nextCalled = false;
        var middleware = new StripeAppAuthMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Options.Create(new StripeOptions { AppSigningSecret = appSecret }),
            NullLogger<StripeAppAuthMiddleware>.Instance);

        await using var db = new AppDbContext(_options);
        var tenant = new TenantContext();
        await middleware.InvokeAsync(http, db, tenant);
        return (http, tenant, nextCalled);
    }

    private static string? ErrorCode(HttpContext http)
    {
        http.Response.Body.Position = 0;
        using var doc = JsonDocument.Parse(http.Response.Body);
        return doc.RootElement.GetProperty("error").GetString();
    }

    [Fact]
    public async Task Valid_signature_scopes_tenant_and_continues()
    {
        var body = Body();
        var (http, tenant, nextCalled) = await RunAsync(body: body, signature: Sign(body));

        Assert.True(nextCalled);
        Assert.Equal(_merchantId, tenant.MerchantId);
        Assert.Equal("*", http.Response.Headers.AccessControlAllowOrigin);
    }

    [Fact]
    public async Task Wrong_secret_is_401_invalid_signature()
    {
        var body = Body();
        var (http, tenant, nextCalled) = await RunAsync(
            body: body, signature: Sign(body, secret: "absec_wrong"));

        Assert.False(nextCalled);
        Assert.Null(tenant.MerchantId);
        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.Equal("invalid_signature", ErrorCode(http));
        // Errors carry the CORS header too, or the extension cannot read the status.
        Assert.Equal("*", http.Response.Headers.AccessControlAllowOrigin);
    }

    [Fact]
    public async Task Tampered_body_is_401()
    {
        var (http, _, nextCalled) = await RunAsync(
            body: Body(userId: "usr_attacker"), signature: Sign(Body()));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.Equal("invalid_signature", ErrorCode(http));
    }

    [Fact]
    public async Task Stale_timestamp_outside_tolerance_is_401()
    {
        var body = Body();
        var stale = DateTimeOffset.UtcNow.AddSeconds(-400).ToUnixTimeSeconds();
        var (http, _, nextCalled) = await RunAsync(body: body, signature: Sign(body, timestamp: stale));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.Equal("invalid_signature", ErrorCode(http));
    }

    [Fact]
    public async Task Missing_signature_header_is_401()
    {
        var (http, _, nextCalled) = await RunAsync(body: Body());

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.Equal("invalid_signature", ErrorCode(http));
    }

    [Fact]
    public async Task Unknown_account_is_404_merchant_not_connected()
    {
        var body = Body(accountId: "acct_never_connected");
        var (http, tenant, nextCalled) = await RunAsync(body: body, signature: Sign(body));

        Assert.False(nextCalled);
        Assert.Null(tenant.MerchantId);
        Assert.Equal(StatusCodes.Status404NotFound, http.Response.StatusCode);
        Assert.Equal("merchant_not_connected", ErrorCode(http));
    }

    [Fact]
    public async Task Empty_signing_secret_is_503_and_verification_never_runs()
    {
        // Even a request signed with the real secret is refused while none is configured.
        var body = Body();
        var (http, _, nextCalled) = await RunAsync(body: body, signature: Sign(body), appSecret: "");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, http.Response.StatusCode);
        Assert.Equal("app_not_configured", ErrorCode(http));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"user_id\":\"usr_1\"}")]
    [InlineData("{\"user_id\":\"usr_1\",\"account_id\":\"cus_not_an_account\"}")]
    public async Task Signed_but_malformed_body_is_400_invalid_request(string body)
    {
        var (http, _, nextCalled) = await RunAsync(body: body, signature: Sign(body));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, http.Response.StatusCode);
        Assert.Equal("invalid_request", ErrorCode(http));
    }

    [Fact]
    public async Task Options_preflight_gets_cors_answer_without_auth()
    {
        var (http, _, nextCalled) = await RunAsync(method: "OPTIONS");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status204NoContent, http.Response.StatusCode);
        Assert.Equal("*", http.Response.Headers.AccessControlAllowOrigin);
        Assert.Equal("POST, OPTIONS", http.Response.Headers.AccessControlAllowMethods);
        Assert.Equal("Content-Type, Stripe-Signature", http.Response.Headers.AccessControlAllowHeaders);
    }

    [Fact]
    public async Task Non_post_is_405()
    {
        var (http, _, nextCalled) = await RunAsync(method: "GET");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status405MethodNotAllowed, http.Response.StatusCode);
        Assert.Equal("method_not_allowed", ErrorCode(http));
        Assert.Equal("*", http.Response.Headers.AccessControlAllowOrigin);
    }
}
