using Microsoft.AspNetCore.Mvc;
using RecoverFlow.Application.Dashboard;

namespace RecoverFlow.Api.Controllers;

/// <summary>
/// Read-only merchant dashboard. Taking merchantId straight from the route is a
/// stopgap until auth lands and the merchant is resolved from the caller's identity.
/// </summary>
[ApiController]
[Route("v1/merchants/{merchantId:guid}")]
public sealed class MerchantDashboardController(MerchantDashboardService dashboard) : ControllerBase
{
    [HttpGet("recovery-cases")]
    public async Task<IActionResult> GetRecoveryCases(
        Guid merchantId,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var result = await dashboard.GetRecoveryCasesAsync(merchantId, page, pageSize, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(Guid merchantId, CancellationToken ct)
    {
        var result = await dashboard.GetDashboardAsync(merchantId, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
