using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Audit;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Connect;

namespace RecoverFlow.Api.Controllers;

/// <summary>
/// The Stripe Marketplace "Retry Waste Audit" app: read-only, one-shot, no account created.
///
/// Deliberately a separate controller from <see cref="StripeConnectController"/> rather than a
/// flag on it. That one requests read_write and persists an encrypted token against a Merchant
/// row; this one requests read_only, creates nothing, and stores nothing. Keeping the two paths
/// physically apart means no future refactor of the product's connect flow can accidentally give
/// the audit write access or leave a token behind.
/// </summary>
[ApiController]
[Route("connect/stripe/audit")]
public sealed class StripeAuditController(
    IOptions<StripeOptions> stripeOptions,
    IOptions<BacktestOptions> backtestOptions,
    IDataProtectionProvider dataProtection,
    IStripeOAuthClient oauthClient,
    IRetryWasteReader reader,
    IEmailSender email,
    ILogger<StripeAuditController> log) : ControllerBase
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Long enough to type an email address after installing, short enough to be useless later.</summary>
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(30);

    // Distinct purpose strings: an audit state token can never be replayed against the
    // write-scoped connect flow, nor a post-install ticket against the OAuth state.
    private readonly IDataProtector protector = dataProtection.CreateProtector("StripeAuditOAuthState");
    private readonly IDataProtector ticketProtector = dataProtection.CreateProtector("StripeAuditPostInstallTicket");

    private sealed record AuditState(string Nonce, string Email, string? CompanyName, DateTime IssuedAtUtc);

    private sealed record AuditTicket(string StripeAccountId, DateTime IssuedAtUtc);

    [HttpGet("authorize")]
    public IActionResult Authorize([FromQuery] string email, [FromQuery] string? companyName)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest("email is required so we know where to send the report.");

        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var state = protector.Protect(JsonSerializer.Serialize(
            new AuditState(nonce, email, companyName, DateTime.UtcNow)));

        // read_only is the entire product promise. Stripe enforces it server-side, so even a
        // bug on our end cannot turn this into a write-capable grant.
        var authorizeUrl = "https://connect.stripe.com/oauth/authorize" +
            "?response_type=code" +
            $"&client_id={Uri.EscapeDataString(stripeOptions.Value.ClientId)}" +
            "&scope=read_only" +
            $"&redirect_uri={Uri.EscapeDataString(AuditRedirectUri())}" +
            $"&state={Uri.EscapeDataString(state)}";

        return Redirect(authorizeUrl);
    }

    /// <summary>
    /// Serves two entry paths. Our own /authorize sends a signed state carrying the email, so the
    /// report can go out immediately. A Marketplace install is initiated by Stripe, arrives with a
    /// code and no state of ours, and has no email attached — that path detours through a page that
    /// asks for one. Rejecting a stateless callback would break every real install.
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string? state, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code)) return BadRequest();

        AuditState? auditState = null;
        if (!string.IsNullOrEmpty(state))
        {
            try
            {
                auditState = JsonSerializer.Deserialize<AuditState>(protector.Unprotect(state));
            }
            catch (CryptographicException)
            {
                log.LogWarning("Rejected audit callback with an invalid or tampered state parameter");
                return BadRequest("Invalid state.");
            }

            if (DateTime.UtcNow - auditState!.IssuedAtUtc > StateLifetime)
                return BadRequest("State expired, please restart the audit.");
        }

        // The code is exchanged only to learn WHICH account authorized us. Reads then go through
        // the platform key plus the Stripe-Account header, so the merchant's access token is never
        // stored, never written to the database, and goes out of scope with this local.
        var token = await oauthClient.ExchangeCodeAsync(code, ct);

        if (auditState is null)
        {
            // Marketplace install: hand the browser a short-lived ticket naming the account, and
            // ask for an address on the next page. The ticket carries no token and no PII.
            var ticket = ticketProtector.Protect(JsonSerializer.Serialize(
                new AuditTicket(token.StripeAccountId, DateTime.UtcNow)));
            return Redirect($"/audit-email.html?t={Uri.EscapeDataString(ticket)}");
        }

        await RunAndSendAsync(token.StripeAccountId, auditState.Email, auditState.CompanyName, ct);
        return Redirect("/audit-sent.html");
    }

    /// <summary>
    /// Completes a Marketplace install once the merchant supplies an address. The ticket is signed
    /// and short-lived, which is what stops it being used to mail arbitrary reports to third
    /// parties long after an install.
    /// </summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run([FromForm] string t, [FromForm] string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(t) || string.IsNullOrWhiteSpace(email))
            return BadRequest("Both the ticket and an email address are required.");

        AuditTicket ticket;
        try
        {
            ticket = JsonSerializer.Deserialize<AuditTicket>(ticketProtector.Unprotect(t))!;
        }
        catch (CryptographicException)
        {
            return BadRequest("Invalid ticket.");
        }

        if (DateTime.UtcNow - ticket.IssuedAtUtc > TicketLifetime)
            return BadRequest("This link has expired. Re-open the audit from your Stripe dashboard.");

        await RunAndSendAsync(ticket.StripeAccountId, email, companyName: null, ct);
        return Redirect("/audit-sent.html");
    }

    private async Task RunAndSendAsync(string stripeAccountId, string to, string? companyName, CancellationToken ct)
    {
        var windowDays = backtestOptions.Value.WindowDays;
        var charges = await reader.ListFailedChargesAsync(
            stripeAccountId, DateTime.UtcNow.AddDays(-windowDays), MaxChargesScanned, ct);

        var report = RetryWasteAuditService.Build(charges, windowDays);
        var content = RetryWasteReportEmail.Render(report, companyName);
        await email.SendAsync(to, content.Subject, content.Html, content.PlainText, ct);

        log.LogInformation(
            "Retry waste audit for {StripeAccountId}: {Failed} failed charges, {Wasted:0}% wasted, report sent",
            stripeAccountId, report.TotalFailedCount, report.WastedPercent);
    }

    /// <summary>
    /// Caps how many charges the scan will page through. A busy account is mostly successes, and
    /// this audit runs inline on the callback, so an unbounded scan would stall the redirect and
    /// chew through the account's rate limit.
    /// </summary>
    private const int MaxChargesScanned = 2_000;

    // The audit has its own registered redirect URI, distinct from the product's OAuth callback.
    private string AuditRedirectUri()
    {
        var configured = stripeOptions.Value.AuditRedirectUri;
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        // Fall back to deriving it from the product redirect so a deployment that has not set the
        // new variable yet still points somewhere correct rather than at the write-scoped callback.
        var product = stripeOptions.Value.OAuthRedirectUri;
        return product.EndsWith("/callback", StringComparison.Ordinal)
            ? product[..^"/callback".Length] + "/audit/callback"
            : product;
    }
}
