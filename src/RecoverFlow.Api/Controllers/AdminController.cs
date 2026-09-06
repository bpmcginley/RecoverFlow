using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Admin;
using RecoverFlow.Application.Common;

namespace RecoverFlow.Api.Controllers;

/// <summary>
/// Read-only internal view across every merchant: signups, connections, recoveries,
/// billing and the plumbing behind them. Not merchant-facing: the cookie auth used by
/// /v1/me is scoped to one merchant, and this deliberately reads across all of them,
/// so it uses its own key instead. The figures themselves come from
/// <see cref="AdminStatsService"/>; this class only decides who may see them.
/// </summary>
[ApiController]
[Route("v1/admin")]
public sealed class AdminController(
    AdminStatsService stats,
    IOptions<AdminOptions> adminOptions) : ControllerBase
{
    private const string HeaderName = "X-Admin-Key";
    private readonly string _key = adminOptions.Value.ApiKey;

    /// <summary>
    /// 404 when unconfigured so an untouched deployment gives nothing away, and a
    /// fixed-time comparison so the response time cannot be used to guess the key.
    /// </summary>
    private IActionResult? Reject()
    {
        if (string.IsNullOrEmpty(_key)) return NotFound();

        var supplied = Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(supplied)) return Unauthorized();

        var a = Encoding.UTF8.GetBytes(supplied);
        var b = Encoding.UTF8.GetBytes(_key);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b)
            ? null
            : Unauthorized();
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct, int days = 30)
    {
        if (Reject() is { } fail) return fail;
        return Ok(await stats.BuildAsync(days, ct));
    }
}
