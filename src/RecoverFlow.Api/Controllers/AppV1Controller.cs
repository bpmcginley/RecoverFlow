using Microsoft.AspNetCore.Mvc;
using RecoverFlow.Api.Middleware;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Dashboard;
using RecoverFlow.Domain;

namespace RecoverFlow.Api.Controllers;

/// <summary>
/// Read-only endpoints backing the embedded Stripe Dashboard app. By the time an action
/// runs, <see cref="StripeAppAuthMiddleware"/> has verified the request signature and
/// scoped the tenant context to the merchant resolved from the signed account_id.
/// Everything is POST so the signed JSON body travels with every call; endpoint
/// parameters ride the route or query string, keeping the signed payload constant.
/// </summary>
[ApiController]
[Route("app/v1")]
public sealed class AppV1Controller(MerchantDashboardService dashboard, ITenantContext tenant) : ControllerBase
{
    private Guid MerchantId => tenant.MerchantId
        ?? throw new InvalidOperationException("AppV1 reached without a resolved merchant; middleware missing?");

    [HttpPost("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) =>
        await dashboard.GetDashboardAsync(MerchantId, ct) is { } d ? Ok(d) : MerchantNotConnected();

    [HttpPost("cases")]
    public async Task<IActionResult> Cases(
        CancellationToken ct,
        [FromQuery] string? page = null,
        [FromQuery] string? pageSize = null,
        [FromQuery] string? status = null)
    {
        // Bound as strings and parsed by hand so a bad value gets the app error shape,
        // not MVC's automatic ProblemDetails 400.
        if (!TryParseInt(page, fallback: 1, out var pageNumber) || pageNumber < 1)
            return InvalidRequest("page must be a positive integer.");
        if (!TryParseInt(pageSize, fallback: 25, out var size))
            return InvalidRequest("pageSize must be an integer.");

        RecoveryStatus? filter = null;
        if (!string.IsNullOrEmpty(status))
        {
            // The round-trip check rejects numeric strings Enum.TryParse would accept;
            // IsDefined rejects undefined values like "99" whose round-trip matches.
            if (!Enum.TryParse<RecoveryStatus>(status, out var parsed) || parsed.ToString() != status || !Enum.IsDefined(parsed))
                return InvalidRequest("status must be one of ActiveRecovery, Recovered, Lost, Cancelled.");
            filter = parsed;
        }

        var result = await dashboard.GetRecoveryCasesAsync(MerchantId, pageNumber, size, filter, ct);
        return result is null ? MerchantNotConnected() : Ok(result);
    }

    [HttpPost("customers/{stripeCustomerId}/cases")]
    public async Task<IActionResult> CustomerCases(string stripeCustomerId, CancellationToken ct) =>
        // No recovery activity for a customer is a normal state, so an empty list is a 200.
        Ok(await dashboard.GetCustomerCasesAsync(MerchantId, stripeCustomerId, ct));

    [HttpPost("invoices/{stripeInvoiceId}")]
    public async Task<IActionResult> InvoiceCase(string stripeInvoiceId, CancellationToken ct) =>
        await dashboard.GetCaseByInvoiceAsync(MerchantId, stripeInvoiceId, ct) is { } c ? Ok(c) : CaseNotFound();

    [HttpPost("cases/{caseId}/dunning")]
    public async Task<IActionResult> Dunning(string caseId, CancellationToken ct)
    {
        // A malformed id cannot name a case, so it gets the same answer as an unknown one.
        if (!Guid.TryParse(caseId, out var id)) return CaseNotFound();
        return await dashboard.GetDunningProgressAsync(MerchantId, id, ct) is { } p ? Ok(p) : CaseNotFound();
    }

    private static bool TryParseInt(string? raw, int fallback, out int value)
    {
        if (string.IsNullOrEmpty(raw)) { value = fallback; return true; }
        return int.TryParse(raw, out value);
    }

    private BadRequestObjectResult InvalidRequest(string message) =>
        BadRequest(new { error = "invalid_request", message });

    private NotFoundObjectResult MerchantNotConnected() => NotFound(new
    {
        error = "merchant_not_connected",
        message = "This Stripe account is not connected to RecoverFlow. Connect it at recoverflow.org first.",
    });

    private NotFoundObjectResult CaseNotFound() => NotFound(new
    {
        error = "case_not_found",
        message = "RecoverFlow has no recovery case matching that id.",
    });
}
