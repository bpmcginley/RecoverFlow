using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Audit;
using RecoverFlow.Application.Common;

namespace RecoverFlow.Api.Controllers;

/// <summary>
/// Callback for the read-only "Retry Waste Audit" Stripe App. A merchant installs the app from
/// the marketplace; Stripe redirects here with an authorization code. We exchange it, run the
/// audit read-only, email the report, and send the merchant back to the marketing site. No token
/// and no merchant data are stored.
///
/// Unlike <see cref="StripeConnectController"/>, this flow is originated by Stripe, not by us, so
/// there is no self-issued `state` to validate — the code exchange is the authentication.
/// </summary>
[ApiController]
[Route("connect/stripe/audit")]
public sealed class RetryWasteAuditController(
    RetryWasteAuditRunner runner,
    IOptions<AuditOptions> options,
    ILogger<RetryWasteAuditController> log) : ControllerBase
{
    private readonly AuditOptions _opts = options.Value;

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code))
            return Redirect(WithStatus("error"));

        try
        {
            var outcome = await runner.RunFromCodeAsync(code, ct);
            return Redirect(WithStatus(outcome.Delivered ? "sent" : "no-email"));
        }
        catch (Exception ex)
        {
            // Always hand the merchant back a clean page rather than a raw 500; the detail is logged.
            log.LogError(ex, "Retry waste audit callback failed");
            return Redirect(WithStatus("error"));
        }
    }

    private string WithStatus(string status)
    {
        var url = _opts.ResultRedirectUrl;
        var sep = url.Contains('?') ? '&' : '?';
        return $"{url}{sep}audit={Uri.EscapeDataString(status)}";
    }
}
