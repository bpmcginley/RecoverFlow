using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain;

namespace RecoverFlow.Api.Controllers;

/// <summary>
/// Read-only internal view of signups and connect-time backtests.
/// Not merchant-facing: the cookie auth used by /v1/me is scoped to one merchant,
/// and this deliberately reads across all of them, so it uses its own key instead.
/// </summary>
[ApiController]
[Route("v1/admin")]
public sealed class AdminController(
    IAppDbContext db,
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

        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.AddDays(-days);

        // IgnoreQueryFilters throughout: the tenant filter exists to stop merchants
        // seeing each other, and this endpoint's whole purpose is the cross-tenant view.
        var merchants = await db.Merchants.IgnoreQueryFilters()
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new
            {
                m.Id,
                m.Email,
                m.CompanyName,
                m.Plan,
                m.CreatedAt,
                m.StripeAccountId,
                // Both halves are needed. The token is never cleared, so on its own it says
                // "installed at some point"; the timestamp is what an uninstall or a refused
                // refresh sets, and a reinstall clears.
                Connected = m.EncryptedStripeAccessToken != null && m.DisconnectedAtUtc == null,
                m.DisconnectedAtUtc,
            })
            .ToListAsync(ct);

        var backtests = await db.AccountBacktests.IgnoreQueryFilters()
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => new
            {
                b.MerchantId,
                Status = b.Status.ToString(),
                b.FailedInvoiceCount,
                b.FailedAmountCents,
                b.RecoverableLowCents,
                b.RecoverableHighCents,
                b.Currency,
                b.CreatedAtUtc,
            })
            .ToListAsync(ct);

        // Grouped by currency as well as status. Cents in different currencies are not
        // addable, and the page used to add them and label the result in dollars.
        //
        // Status is named in memory rather than in the query. Nothing configures a string enum
        // converter, so the enum used to reach the page as an integer and the page's filter
        // for "Recovered" matched nothing: the recovered total was always zero.
        var payments = (await db.FailedPayments.IgnoreQueryFilters()
            .GroupBy(p => new { p.Status, p.Currency })
            .Select(g => new { g.Key.Status, g.Key.Currency, Count = g.Count(), Cents = g.Sum(x => x.AmountCents) })
            .ToListAsync(ct))
            .Select(g => new { Status = g.Status.ToString(), g.Currency, g.Count, g.Cents })
            .ToList();

        // One scan per account, not every scan: a merchant who reruns their backtest would
        // otherwise have the same failed revenue counted once per run. Their newest complete
        // run is their current picture; an account with no complete run contributes nothing.
        var latestScanPerAccount = backtests
            .Where(b => b.Status == nameof(BacktestStatus.Complete))
            .GroupBy(b => b.MerchantId)
            .Select(g => g.OrderByDescending(b => b.CreatedAtUtc).First())
            .ToList();

        var failedAmountScanned = latestScanPerAccount
            .GroupBy(b => b.Currency)
            .Select(g => new { currency = g.Key, cents = g.Sum(b => b.FailedAmountCents) })
            .OrderByDescending(x => x.cents)
            .ToList();

        var signupsByDay = merchants
            .Where(m => m.CreatedAt >= since)
            .GroupBy(m => m.CreatedAt.Date)
            .OrderBy(g => g.Key)
            .Select(g => new { date = g.Key.ToString("yyyy-MM-dd"), count = g.Count() })
            .ToList();

        return Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            windowDays = days,
            totals = new
            {
                merchants = merchants.Count,
                connected = merchants.Count(m => m.Connected),
                disconnected = merchants.Count(m => m.DisconnectedAtUtc != null),
                signupsInWindow = merchants.Count(m => m.CreatedAt >= since),
                backtests = backtests.Count,
                backtestsComplete = backtests.Count(b => b.Status == nameof(BacktestStatus.Complete)),
                accountsScanned = latestScanPerAccount.Count,
                failedAmountScanned,
            },
            signupsByDay,
            recoveryCases = payments,
            merchants,
            backtests,
        });
    }
}
