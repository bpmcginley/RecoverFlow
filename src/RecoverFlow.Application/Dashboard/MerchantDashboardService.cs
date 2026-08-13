using Microsoft.EntityFrameworkCore;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;

namespace RecoverFlow.Application.Dashboard;

/// <summary>Read-only queries backing the merchant dashboard.</summary>
public sealed class MerchantDashboardService(IAppDbContext db)
{
    public const int MaxPageSize = 100;

    /// <summary>A single end customer rarely accumulates more than a handful of cases,
    /// so the per-customer view takes a fixed cap instead of paging.</summary>
    public const int MaxCustomerCases = 25;

    /// <summary>Newest-first page of the merchant's recovery cases, optionally filtered
    /// by status; null for an unknown merchant.</summary>
    public async Task<PagedResult<RecoveryCaseSummary>?> GetRecoveryCasesAsync(
        Guid merchantId, int page, int pageSize, RecoveryStatus? status = null, CancellationToken ct = default)
    {
        if (!await db.Merchants.AnyAsync(m => m.Id == merchantId, ct)) return null;

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var cases = db.FailedPayments.Where(p => p.MerchantId == merchantId);
        if (status is { } s) cases = cases.Where(p => p.Status == s);

        var total = await cases.CountAsync(ct);
        var items = await ProjectSummariesAsync(
            cases.OrderByDescending(p => p.FirstFailedAt).Skip((page - 1) * pageSize).Take(pageSize), ct);

        return new(items, page, pageSize, total);
    }

    /// <summary>Newest-first cases for one Stripe customer, capped at <see cref="MaxCustomerCases"/>.
    /// An empty list is a normal answer, not an error.</summary>
    public async Task<CaseList> GetCustomerCasesAsync(
        Guid merchantId, string stripeCustomerId, CancellationToken ct = default)
    {
        var cases = db.FailedPayments
            .Where(p => p.MerchantId == merchantId && p.StripeCustomerId == stripeCustomerId);

        var total = await cases.CountAsync(ct);
        var items = await ProjectSummariesAsync(
            cases.OrderByDescending(p => p.FirstFailedAt).Take(MaxCustomerCases), ct);

        return new(items, total);
    }

    /// <summary>The recovery case opened for a Stripe invoice; null when none was.
    /// At most one case per invoice, guaranteed by the (MerchantId, StripeInvoiceId) index.</summary>
    public async Task<RecoveryCaseSummary?> GetCaseByInvoiceAsync(
        Guid merchantId, string stripeInvoiceId, CancellationToken ct = default)
    {
        var items = await ProjectSummariesAsync(
            db.FailedPayments.Where(p => p.MerchantId == merchantId && p.StripeInvoiceId == stripeInvoiceId), ct);
        return items.SingleOrDefault();
    }

    /// <summary>Dunning-email progress for one case; null when the case is unknown or
    /// belongs to another merchant.</summary>
    public async Task<DunningProgress?> GetDunningProgressAsync(
        Guid merchantId, Guid caseId, CancellationToken ct = default)
    {
        if (!await db.FailedPayments.AnyAsync(p => p.Id == caseId && p.MerchantId == merchantId, ct))
            return null;

        var entries = await db.EmailSequences
            .Where(e => e.FailedPaymentId == caseId)
            .OrderBy(e => e.SequenceStep)
            .Select(e => new DunningEntry(
                e.SequenceStep, e.EmailType, e.SentAt, e.OpenedAt, e.ClickedAt, e.ResultedInRecovery))
            .ToListAsync(ct);

        return new(caseId, entries);
    }

    /// <summary>Runs the shared case projection over an already-filtered-and-ordered query.
    /// Two steps because enum-to-string isn't translatable: SQL fetches a slim row,
    /// memory shapes the record.</summary>
    private static async Task<List<RecoveryCaseSummary>> ProjectSummariesAsync(
        IQueryable<FailedPayment> cases, CancellationToken ct)
    {
        var rows = await cases
            .Select(p => new
            {
                p.Id, p.StripeInvoiceId, p.CustomerEmail, p.AmountCents, p.Currency,
                p.Status, p.FirstFailedAt, p.RecoveredAt, p.LostAt,
                p.ReversedAmountCents, p.ReversalReason,
                AttemptCount = p.RetryAttempts.Count,
            })
            .ToListAsync(ct);

        return rows.Select(r => new RecoveryCaseSummary(
            r.Id, r.StripeInvoiceId, r.CustomerEmail, r.AmountCents, r.Currency,
            r.Status.ToString(), r.FirstFailedAt, r.RecoveredAt ?? r.LostAt, r.AttemptCount,
            r.ReversedAmountCents, r.ReversalReason)).ToList();
    }

    /// <summary>All-time and last-30-days summary stats; null for an unknown merchant.</summary>
    public async Task<MerchantDashboard?> GetDashboardAsync(Guid merchantId, CancellationToken ct = default)
    {
        if (!await db.Merchants.AnyAsync(m => m.Id == merchantId, ct)) return null;

        // Aggregated in memory over a slim projection: fine at current volumes,
        // and keeps the windowing logic a pure, unit-testable function.
        var cases = await db.FailedPayments
            .Where(p => p.MerchantId == merchantId)
            .Select(p => new CaseSnapshot(
                p.Status, p.AmountCents, p.FirstFailedAt, p.RecoveredAt, p.LostAt, p.ReversedAmountCents))
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
        var won = windowed.Where(c => c.Status == RecoveryStatus.Recovered).ToList();
        // Reversals are clamped at the case amount by the webhook handler, but a dispute
        // amount carries the network fee, so clamp again rather than trust the stored value.
        var gross = won.Sum(c => c.AmountCents);
        var reversed = won.Sum(c => Math.Clamp(c.ReversedCents, 0, c.AmountCents));

        return new(
            byStatus,
            // Net, so the headline agrees with the invoice: we bill on what the merchant kept.
            AmountRecoveredCents: gross - reversed,
            AmountAtRiskCents: windowed.Where(c => c.Status == RecoveryStatus.ActiveRecovery).Sum(c => c.AmountCents),
            RecoveryRate: closed == 0 ? 0 : (double)recovered / closed,
            AmountRecoveredGrossCents: gross,
            AmountReversedCents: reversed);

        static DateTime DefiningMoment(CaseSnapshot c) => c.Status switch
        {
            RecoveryStatus.Recovered => c.RecoveredAt ?? c.OpenedAt,
            RecoveryStatus.Lost => c.LostAt ?? c.OpenedAt,
            _ => c.OpenedAt,
        };
    }
}
