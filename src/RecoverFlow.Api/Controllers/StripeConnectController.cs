using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Backtest;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Connect;

namespace RecoverFlow.Api.Controllers;

/// <summary>
/// Stripe Apps OAuth 2.0 install: a merchant installs RecoverFlow onto their existing Stripe
/// account from the App Marketplace listing. RecoverFlow never creates or manages connected
/// accounts. The route keeps its /connect/stripe prefix because that URL is already registered
/// with Stripe as an allowed redirect and is baked into live merchants' bookmarks.
/// </summary>
[ApiController]
[Route("connect/stripe")]
public sealed class StripeConnectController(
    IOptions<StripeOptions> stripeOptions,
    IDataProtectionProvider dataProtection,
    StripeConnectService connectService,
    AccountBacktestService backtests,
    IBacktestJobScheduler backtestJobs,
    IAppDbContext db,
    ILogger<StripeConnectController> log) : ControllerBase
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);
    private readonly IDataProtector protector = dataProtection.CreateProtector("StripeConnectOAuthState");
    // Separate purpose string: a backtest-view token can never be replayed as OAuth state.
    private readonly IDataProtector backtestProtector = dataProtection.CreateProtector("StripeConnectBacktestView");

    private sealed record OAuthState(string Nonce, string Email, string CompanyName, DateTime IssuedAtUtc);

    [HttpGet("authorize")]
    public IActionResult Authorize([FromQuery] string email, [FromQuery] string companyName)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(companyName))
            return BadRequest("email and companyName are required.");

        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var state = new OAuthState(nonce, email, companyName, DateTime.UtcNow);
        var protectedState = protector.Protect(JsonSerializer.Serialize(state));

        // Stripe Apps OAuth 2.0, not Connect OAuth. App review rejected v0.0.2 because a merchant
        // arriving from the Marketplace listing was being sent to a Connect authorize URL, which
        // is not an app install and shows "The provided OAuth link is invalid".
        //
        // This link takes no response_type and no scope. Under Stripe Apps the grant is whatever
        // stripe-app.json declares, so the consent screen is driven by the manifest permissions
        // and adding a scope here would do nothing. Widening access now means editing the
        // manifest, in public, where merchants can read the purpose strings.
        var authorizeUrl = "https://marketplace.stripe.com/oauth/v2/authorize" +
            $"?client_id={Uri.EscapeDataString(stripeOptions.Value.AppClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(stripeOptions.Value.OAuthRedirectUri)}" +
            $"&state={Uri.EscapeDataString(protectedState)}";

        return Redirect(authorizeUrl);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return BadRequest();

        OAuthState oauthState;
        try
        {
            var json = protector.Unprotect(state);
            oauthState = JsonSerializer.Deserialize<OAuthState>(json)!;
        }
        catch (CryptographicException)
        {
            log.LogWarning("Rejected Stripe Connect callback with an invalid or tampered state parameter");
            return BadRequest("Invalid state.");
        }

        if (DateTime.UtcNow - oauthState.IssuedAtUtc > StateLifetime)
        {
            log.LogWarning("Rejected Stripe Connect callback with an expired state parameter");
            return BadRequest("State expired, please restart the connection.");
        }

        var merchant = await connectService.CompleteConnectionAsync(
            code, oauthState.Email, oauthState.CompanyName, ct);

        log.LogInformation("Connected merchant {MerchantId} to Stripe account {StripeAccountId}",
            merchant.Id, merchant.StripeAccountId);

        // Kick off the "here's what you lost" scan out-of-band, and hand the confirmation page a
        // signed token so it can poll for the result without us exposing the merchant's GUID.
        var backtestId = await backtests.BeginAsync(merchant.Id, ct);
        backtestJobs.Enqueue(backtestId);
        var token = Uri.EscapeDataString(backtestProtector.Protect(backtestId.ToString()));

        return Redirect($"/connected.html?b={token}");
    }

    /// <summary>Polled by connected.html to render the backtest as soon as the scan finishes.</summary>
    [HttpGet("backtest")]
    public async Task<IActionResult> Backtest([FromQuery] string token, CancellationToken ct)
    {
        Guid backtestId;
        try
        {
            backtestId = Guid.Parse(backtestProtector.Unprotect(token));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return BadRequest();
        }

        var backtest = await db.AccountBacktests.FirstOrDefaultAsync(b => b.Id == backtestId, ct);
        return backtest is null ? NotFound() : Ok(AccountBacktestService.ToView(backtest));
    }
}
