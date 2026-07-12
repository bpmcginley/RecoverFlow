using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecoverFlow.Api.Auth;
using RecoverFlow.Application.Dashboard;

namespace RecoverFlow.Api.Controllers;

/// <summary>
/// The signed-in merchant's dashboard. The merchant is resolved from the auth cookie,
/// and the tenant query filter (fed from the same claim) scopes every read — so there's
/// no merchant id in the route to tamper with.
/// </summary>
[ApiController]
[Route("v1/me")]
[Authorize]
public sealed class MerchantDashboardController(MerchantDashboardService dashboard) : ControllerBase
{
    [HttpGet("recovery-cases")]
    public async Task<IActionResult> GetRecoveryCases(
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        if (User.GetMerchantId() is not { } merchantId) return Unauthorized();

        var result = await dashboard.GetRecoveryCasesAsync(merchantId, page, pageSize, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        if (User.GetMerchantId() is not { } merchantId) return Unauthorized();

        var result = await dashboard.GetDashboardAsync(merchantId, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
