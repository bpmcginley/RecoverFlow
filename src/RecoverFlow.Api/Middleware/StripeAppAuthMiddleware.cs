using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;

namespace RecoverFlow.Api.Middleware;

/// <summary>
/// Authenticates requests from the embedded Stripe Dashboard app (/app/v1). The app's UI
/// extension signs the fixed JSON body {"user_id":...,"account_id":...} with the app's
/// signing secret (fetchStripeSignature) and sends the result as a Stripe-Signature
/// header. That is the same t=/v1= scheme Stripe uses for webhooks, so Stripe.net's
/// verifier does the checking, with the standard 300 second timestamp tolerance. On
/// success the acct_ id resolves to a Merchant and the tenant context is scoped to it,
/// mirroring what <see cref="TenantResolutionMiddleware"/> does from the session cookie;
/// the cookie path is never involved here.
///
/// UI extensions run from a null origin, so this branch, and only this branch, answers
/// OPTIONS preflights and stamps Access-Control-Allow-Origin: * on every response,
/// errors included. Without the header the extension cannot even read the status code.
/// </summary>
public sealed class StripeAppAuthMiddleware(
    RequestDelegate next,
    IOptions<StripeOptions> stripe,
    ILogger<StripeAppAuthMiddleware> log)
{
    private const long SignatureToleranceSeconds = 300;

    // The real signed payload is ~80 bytes; anything past a few KB is not a Stripe app
    // request, and capping it stops unauthenticated callers burning memory on the read.
    private const long MaxBodyBytes = 4 * 1024;

    public async Task InvokeAsync(HttpContext http, IAppDbContext db, TenantContext tenant)
    {
        var headers = http.Response.Headers;
        headers.AccessControlAllowOrigin = "*";

        if (HttpMethods.IsOptions(http.Request.Method))
        {
            headers.AccessControlAllowMethods = "POST, OPTIONS";
            headers.AccessControlAllowHeaders = "Content-Type, Stripe-Signature";
            http.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        if (!HttpMethods.IsPost(http.Request.Method))
        {
            await WriteError(http, StatusCodes.Status405MethodNotAllowed,
                "method_not_allowed", "App endpoints accept POST only.");
            return;
        }

        // The signing secret only exists after the app's first upload to Stripe. Until it
        // is configured there is nothing to verify against, so refuse rather than skip.
        var secret = stripe.Value.AppSigningSecret;
        if (string.IsNullOrEmpty(secret))
        {
            await WriteError(http, StatusCodes.Status503ServiceUnavailable,
                "app_not_configured", "The Stripe app is not configured on this server yet.");
            return;
        }

        if (http.Request.ContentLength is > MaxBodyBytes)
        {
            await WriteError(http, StatusCodes.Status400BadRequest,
                "invalid_request", "The request body is larger than an app request can be.");
            return;
        }
        if (http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>()
            is { IsReadOnly: false } sizeFeature)
        {
            sizeFeature.MaxRequestBodySize = MaxBodyBytes;
        }

        var json = await new StreamReader(http.Request.Body).ReadToEndAsync(http.RequestAborted);
        var signature = http.Request.Headers["Stripe-Signature"].ToString();
        try
        {
            Stripe.EventUtility.ValidateSignature(json, signature, secret, SignatureToleranceSeconds);
        }
        catch (Exception ex) when (ex is Stripe.StripeException or ArgumentException)
        {
            // One code for a missing, malformed, stale, or wrong signature: probes learn nothing.
            log.LogWarning("Rejected app request with an unverifiable Stripe-Signature: {Reason}", ex.Message);
            await WriteError(http, StatusCodes.Status401Unauthorized,
                "invalid_signature", "The request signature is missing or invalid.");
            return;
        }

        SignedBody? body;
        try { body = JsonSerializer.Deserialize<SignedBody>(json); }
        catch (JsonException) { body = null; }

        if (body is not { UserId: { Length: > 0 } userId, AccountId: { Length: > 0 } accountId }
            || !accountId.StartsWith("acct_", StringComparison.Ordinal))
        {
            await WriteError(http, StatusCodes.Status400BadRequest,
                "invalid_request", "The request body must carry user_id and an acct_ prefixed account_id.");
            return;
        }

        var merchant = await db.Merchants
            .SingleOrDefaultAsync(m => m.StripeAccountId == accountId, http.RequestAborted);
        if (merchant is null)
        {
            await WriteError(http, StatusCodes.Status404NotFound, "merchant_not_connected",
                "This Stripe account is not connected to RecoverFlow. Connect it at recoverflow.org first.");
            return;
        }

        // user_id is authenticated by the signature but carries no authorization weight:
        // any Dashboard user of an installed account may read. Logged for audit.
        log.LogInformation(
            "Stripe app request: dashboard user {UserId} on {StripeAccountId} as merchant {MerchantId}",
            userId, accountId, merchant.Id);

        tenant.MerchantId = merchant.Id;
        await next(http);
    }

    private static Task WriteError(HttpContext http, int status, string error, string message)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new { error, message }, http.RequestAborted);
    }

    /// <summary>The exact signed payload: field order fixed as user_id then account_id,
    /// and the raw bytes on the wire are the bytes verified, never a re-serialization.</summary>
    private sealed record SignedBody(
        [property: JsonPropertyName("user_id")] string? UserId,
        [property: JsonPropertyName("account_id")] string? AccountId);
}
