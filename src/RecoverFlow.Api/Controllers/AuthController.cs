using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecoverFlow.Api.Auth;
using RecoverFlow.Application.Common;

namespace RecoverFlow.Api.Controllers;

/// <summary>
/// Passwordless (magic-link) sign-in for the customer dashboard. The merchant enters
/// their email, receives a signed link, and clicking it drops a session cookie.
/// </summary>
[ApiController]
[Route("auth")]
public sealed class AuthController(
    IAppDbContext db,
    MagicLinkTokenService tokens,
    IEmailSender email,
    IOptions<AppOptions> appOptions,
    ILogger<AuthController> log) : ControllerBase
{
    private readonly AppOptions app = appOptions.Value;

    public sealed record RequestLinkRequest(string Email);

    [HttpPost("request-link")]
    public async Task<IActionResult> RequestLink([FromBody] RequestLinkRequest req, CancellationToken ct)
    {
        var address = req.Email?.Trim() ?? "";
        if (address.Length == 0) return BadRequest(new { message = "Email is required." });

        var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.Email.ToLower() == address.ToLower(), ct);

        // Mail only a real merchant, but always return the same body so this endpoint
        // can't be used to discover which emails have accounts.
        if (merchant is not null)
        {
            var link = $"{app.DashboardUrl}/auth/verify?token={Uri.EscapeDataString(tokens.Mint(merchant.Id))}";
            var (subject, html, plain) = LoginEmail.Build(merchant.CompanyName, link);
            await email.SendAsync(merchant.Email, subject, html, plain, ct);
            log.LogInformation("Sent magic-link sign-in email to merchant {MerchantId}", merchant.Id);
        }
        else
        {
            log.LogInformation("Magic-link requested for an email with no account — no mail sent");
        }

        return Ok(new { message = "If that email has a RecoverFlow account, a sign-in link is on its way." });
    }

    [HttpGet("verify")]
    public async Task<IActionResult> Verify([FromQuery] string token, CancellationToken ct)
    {
        var merchantId = tokens.Validate(token);
        var merchant = merchantId is null
            ? null
            : await db.Merchants.FirstOrDefaultAsync(m => m.Id == merchantId, ct);

        if (merchant is null)
            return Redirect("/app/login.html?error=expired");

        var claims = new List<Claim>
        {
            new(ClaimsPrincipalExtensions.MerchantIdClaim, merchant.Id.ToString()),
            new(ClaimTypes.Name, merchant.CompanyName),
            new(ClaimTypes.Email, merchant.Email),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        log.LogInformation("Merchant {MerchantId} signed in via magic link", merchant.Id);
        return Redirect("/app/");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    /// <summary>Identity of the signed-in merchant, for the dashboard header.</summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me() => Ok(new
    {
        companyName = User.FindFirst(ClaimTypes.Name)?.Value,
        email = User.FindFirst(ClaimTypes.Email)?.Value,
    });
}
