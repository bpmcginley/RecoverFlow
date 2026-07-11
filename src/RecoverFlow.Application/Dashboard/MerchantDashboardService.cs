using Microsoft.EntityFrameworkCore;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain;

namespace RecoverFlow.Application.Dashboard;

/// <summary>Read-only queries backing the merchant dashboard.</summary>
public sealed class MerchantDashboardService(IAppDbContext db)
{
    public const int MaxPageSize = 100;

    /// <summary>Newest-first page of the merchant's recovery cases; null for an unknown merchant.</summary>
    public async Task<PagedResult<RecoveryCaseSummary>?> GetRecoveryCasesAsync(
        Guid merchantId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!await db.Merchants.AnyAsync(m => m.Id == merchantId, ct)) return null;

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var cases = db.FailedPayments.Where(p => p.MerchantId == merchantId);
        var total = await cases.CountAsync(ct);
        var rows = await cases
            .OrderByDescending(p => p.FirstFailedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id, p.StripeInvoiceId, p.CustomerEmail, p.AmountCents, p.Currency,
                p.Status, p.FirstFailedAt, p.RecoveredAt, p.LostAt,
                AttemptCount = p.RetryAttempts.Count,
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new RecoveryCaseSummary(
            r.Id, r.StripeInvoiceId, r.CustomerEmail, r.AmountCents, r.Currency,
            r.Status.ToString(), r.FirstFailedAt, r.RecoveredAt ?? r.LostAt, r.AttemptCount)).ToList();

        return new(items, page, pageSize, total);
    }

    /// <summary>All-time and last-30-days summary stats; null for an unknown merchant.</summary>
    public async Task<MerchantDashboard?> GetDashboardAsync(Guid merchantId, CancellationToken ct = default)
    {
        if (!await db.Merchants.AnyAsync(m => m.Id == merchantId, ct)) return null;

        // Aggregated in memory over a slim projection: fine at current volumes,
        // and keeps the windowing logic a pure, unit-testable function.
        var cases = await db.FailedPayments
            .Where(p => p.MerchantId == merchantId)
            .Select(p => new CaseSnapshot(p.Status, p.AmountCents, p.FirstFailedAt, p.RecoveredAt, p.LostAt))
            .ToListAsync(ct);

        return new(ComputeStats(cases, since: null), ComputeStats(cases, DateTime.UtcNow.AddDays(-30)));
    }

    /// <summary>
    /// Stats over the cases whose status-defining moment (closure for recovered/lost,
    /// opening otherwise) falls at or after <paramref name="since"/>; null means all-time.
    /// </summary>
    public static DashboardStats ComputeStats(IReadOnlyCollection<CaseSnapshot> cases, DateTime? since)
    {
        var windowed = since is null ? cases : cases.Where(c => DefiningMoment(c) >= since).ToList();

        var byStatus = Enum.GetValues<RecoveryStatus>()
            .ToDictionary(s => s.ToString(), s => windowed.Count(c => c.Status == s));

        var recovered = byStatus[nameof(RecoveryStatus.Recovered)];
        var closed = recovered + byStatus[nameof(RecoveryStatus.Lost)];

        // Amounts sum cents across currencies as-is; a per-currency breakdown
        // can come with multi-currency support.
        return new(
            byStatus,
            AmountRecoveredCents: windowed.Where(c => c.Status == RecoveryStatus.Recovered).Sum(c => c.AmountCents),
            AmountAtRiskCents: windowed.Where(c => c.Status == RecoveryStatus.ActiveRecovery).Sum(c => c.AmountCents),
            RecoveryRate: closed == 0 ? 0 : (double)recovered / closed);

        static DateTime DefiningMoment(CaseSnapshot c) => c.Status switch
        {
            RecoveryStatus.Recovered => c.RecoveredAt ?? c.OpenedAt,
            RecoveryStatus.Lost => c.LostAt ?? c.OpenedAt,
            _ => c.OpenedAt,
        };
    }
}
