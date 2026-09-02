using Microsoft.EntityFrameworkCore;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain;

namespace RecoverFlow.Application.Admin;

/// <summary>
/// The cross-tenant read behind the internal dashboard. Every query ignores the tenant
/// filter on purpose: that filter exists to stop merchants seeing each other, and this
/// view's whole point is to see all of them at once.
///
/// Grouping is done in SQL and the joining in memory. The tables are small enough that
/// the in-memory half is cheap, and it keeps every enum-to-string and currency split
/// out of the query translator.
/// </summary>
public sealed class AdminStatsService(IAppDbContext db)
{
    /// <summary>Newest cases shipped to the page. The drill-down filters these per merchant.</summary>
    public const int CaseLimit = 200;
    public const int InvoiceLimit = 100;

    public async Task<AdminStats> BuildAsync(int days, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var now = DateTime.UtcNow;
        var since = now.AddDays(-days);

        var merchantRows = await db.Merchants.IgnoreQueryFilters()
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new
            {
                m.Id, m.Email, m.CompanyName, m.Plan, m.CreatedAt, m.StripeAccountId,
                Connected = m.EncryptedStripeAccessToken != null && m.DisconnectedAtUtc == null,
                m.DisconnectedAtUtc,
            })
            .ToListAsync(ct);

        var names = merchantRows.ToDictionary(m => m.Id, m => m.CompanyName);
        string NameOf(Guid id) => names.TryGetValue(id, out var n) ? n : "(unknown merchant)";

        var backtests = (await db.AccountBacktests.IgnoreQueryFilters()
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => new
            {
                b.Id, b.MerchantId, b.Status, b.WindowDays, b.FailedInvoiceCount, b.FailedAmountCents,
                b.RecoverableLowCents, b.RecoverableHighCents, b.Currency, b.FailureReason,
                b.CreatedAtUtc, b.CompletedAtUtc,
            })
            .ToListAsync(ct))
            .Select(b => new AdminBacktest(
                b.Id, b.MerchantId, NameOf(b.MerchantId), b.Status.ToString(), b.WindowDays,
                b.FailedInvoiceCount, b.FailedAmountCents, b.RecoverableLowCents, b.RecoverableHighCents,
                b.Currency, b.FailureReason, b.CreatedAtUtc, b.CompletedAtUtc))
            .ToList();

        var caseGroups = (await db.FailedPayments.IgnoreQueryFilters()
            .GroupBy(p => new { p.MerchantId, p.Status, p.Currency })
            .Select(g => new
            {
                g.Key.MerchantId, g.Key.Status, g.Key.Currency,
                Count = g.Count(),
                Cents = g.Sum(x => x.AmountCents),
                Reversed = g.Sum(x => x.ReversedAmountCents),
                Fee = g.Sum(x => x.BilledFeeCents),
            })
            .ToListAsync(ct))
            .Select(g => new CaseGroup(g.MerchantId, g.Status, g.Currency, g.Count, g.Cents, g.Reversed, g.Fee))
            .ToList();

        var caseTimes = await db.FailedPayments.IgnoreQueryFilters()
            .GroupBy(p => p.MerchantId)
            .Select(g => new
            {
                MerchantId = g.Key,
                LastFailed = g.Max(x => x.FirstFailedAt),
                LastRecovered = g.Max(x => x.RecoveredAt),
            })
            .ToDictionaryAsync(t => t.MerchantId, ct);

        var failedInWindow = await db.FailedPayments.IgnoreQueryFilters()
            .Where(p => p.FirstFailedAt >= since)
            .Select(p => p.FirstFailedAt)
            .ToListAsync(ct);

        var recoveredInWindow = await db.FailedPayments.IgnoreQueryFilters()
            .Where(p => p.RecoveredAt >= since)
            .Select(p => new { At = p.RecoveredAt!.Value, p.Currency, p.AmountCents })
            .ToListAsync(ct);

        var cases = (await db.FailedPayments.IgnoreQueryFilters()
            .OrderByDescending(p => p.FirstFailedAt)
            .Take(CaseLimit)
            .Select(p => new
            {
                p.Id, p.MerchantId, p.StripeInvoiceId, p.CustomerEmail, p.AmountCents, p.Currency,
                p.Status, p.FailureType, p.DeclineCode, p.RecoveryMethod, p.FirstFailedAt, p.RecoveredAt,
                p.LostAt, p.ReversedAmountCents, p.ReversalReason, p.BilledAtUtc,
                Retries = p.RetryAttempts.Count(a => a.AttemptedAt != null),
                Emails = p.EmailEntries.Count(),
            })
            .ToListAsync(ct))
            .Select(p => new AdminCase(
                p.Id, p.MerchantId, NameOf(p.MerchantId), p.StripeInvoiceId, p.CustomerEmail,
                p.AmountCents, p.Currency, p.Status.ToString(), p.FailureType.ToString(), p.DeclineCode,
                p.RecoveryMethod.ToString(), p.FirstFailedAt, p.RecoveredAt, p.LostAt,
                p.ReversedAmountCents, p.ReversalReason, p.BilledAtUtc, p.Retries, p.Emails))
            .ToList();

        var declines = (await db.FailedPayments.IgnoreQueryFilters()
            .GroupBy(p => new { p.DeclineCode, p.FailureType })
            .Select(g => new
            {
                g.Key.DeclineCode, g.Key.FailureType,
                Count = g.Count(),
                Recovered = g.Sum(x => x.Status == RecoveryStatus.Recovered ? 1 : 0),
                Lost = g.Sum(x => x.Status == RecoveryStatus.Lost ? 1 : 0),
            })
            .ToListAsync(ct))
            .Select(d => new DeclineBreakdown(
                d.DeclineCode ?? "(none)", d.FailureType.ToString(), d.Count, d.Recovered, d.Lost))
            .OrderByDescending(d => d.Count)
            .ToList();

        var methods = (await db.FailedPayments.IgnoreQueryFilters()
            .Where(p => p.Status == RecoveryStatus.Recovered)
            .GroupBy(p => p.RecoveryMethod)
            .Select(g => new { Method = g.Key, Count = g.Count() })
            .ToListAsync(ct))
            .Select(m => new MethodBreakdown(m.Method.ToString(), m.Count))
            .OrderByDescending(m => m.Count)
            .ToList();

        var retryResults = await db.RetryAttempts.IgnoreQueryFilters()
            .GroupBy(a => a.Result)
            .Select(g => new { Result = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int RetriesWith(string? result) => retryResults.Where(r => r.Result == result).Sum(r => r.Count);
        var retries = new RetryStats(
            Total: retryResults.Sum(r => r.Count),
            Succeeded: RetriesWith("success"),
            Failed: RetriesWith("failed"),
            Skipped: RetriesWith("skipped"),
            Pending: RetriesWith(null));

        var emails = (await db.EmailSequences.IgnoreQueryFilters()
            .GroupBy(e => new { e.SequenceStep, e.EmailType })
            .Select(g => new
            {
                g.Key.SequenceStep, g.Key.EmailType,
                Sent = g.Count(),
                Opened = g.Sum(x => x.OpenedAt != null ? 1 : 0),
                Clicked = g.Sum(x => x.ClickedAt != null ? 1 : 0),
                Recovered = g.Sum(x => x.ResultedInRecovery ? 1 : 0),
            })
            .ToListAsync(ct))
            .OrderBy(e => e.SequenceStep).ThenBy(e => e.EmailType)
            .Select(e => new EmailStepStats(e.SequenceStep, e.EmailType, e.Sent, e.Opened, e.Clicked, e.Recovered))
            .ToList();

        var feeInvoices = (await db.FeeInvoices.IgnoreQueryFilters()
            .OrderByDescending(f => f.CreatedAtUtc)
            .Take(InvoiceLimit)
            .Select(f => new
            {
                f.Id, f.MerchantId, f.PeriodLabel, f.BillableRecoveredCents, f.RecoveredCaseCount, f.FeeCents,
                f.ReversalCreditCents, f.FloorTopUpCents, f.TotalCents, f.Currency, f.Status, f.StripeInvoiceId,
                f.HostedInvoiceUrl, f.FailureReason, f.CreatedAtUtc, f.SentAtUtc,
            })
            .ToListAsync(ct))
            .Select(f => new AdminFeeInvoice(
                f.Id, f.MerchantId, NameOf(f.MerchantId), f.PeriodLabel, f.BillableRecoveredCents,
                f.RecoveredCaseCount, f.FeeCents, f.ReversalCreditCents, f.FloorTopUpCents, f.TotalCents,
                f.Currency, f.Status.ToString(), f.StripeInvoiceId, f.HostedInvoiceUrl, f.FailureReason,
                f.CreatedAtUtc, f.SentAtUtc))
            .ToList();

        var feesInvoiced = (await db.FeeInvoices.IgnoreQueryFilters()
            .Where(f => f.Status == FeeInvoiceStatus.Sent)
            .GroupBy(f => f.Currency)
            .Select(g => new { Currency = g.Key, Cents = g.Sum(f => f.TotalCents) })
            .ToListAsync(ct))
            .Select(x => new Money(x.Currency, x.Cents))
            .ToList();

        // No tenant filter on this table: the ledger is per event, not per merchant.
        var webhookTotal = await db.ProcessedWebhookEvents.CountAsync(ct);
        DateTime? lastWebhook = webhookTotal == 0
            ? null
            : await db.ProcessedWebhookEvents.MaxAsync(w => w.ProcessedAt, ct);
        var webhookWindow = await db.ProcessedWebhookEvents
            .Where(w => w.ProcessedAt >= since)
            .Select(w => new { w.EventType, w.ProcessedAt })
            .ToListAsync(ct);

        // ---- Assemble -------------------------------------------------------------------

        var groupsByMerchant = caseGroups.ToLookup(g => g.MerchantId);
        var latestScan = backtests
            .Where(b => b.Status == nameof(BacktestStatus.Complete))
            .GroupBy(b => b.MerchantId)
            // Already newest-first, so the first complete run per merchant is their current picture.
            .ToDictionary(g => g.Key, g => g.First());

        var merchants = merchantRows.Select(m =>
        {
            var groups = groupsByMerchant[m.Id].ToList();
            caseTimes.TryGetValue(m.Id, out var times);
            return new AdminMerchant(
                m.Id, m.Email, m.CompanyName, m.Plan, m.CreatedAt, m.StripeAccountId,
                m.Connected, m.DisconnectedAtUtc,
                ActiveCases: CountWith(groups, RecoveryStatus.ActiveRecovery),
                RecoveredCases: CountWith(groups, RecoveryStatus.Recovered),
                LostCases: CountWith(groups, RecoveryStatus.Lost),
                Recovered: SumByCurrency(groups.Where(g => g.Status == RecoveryStatus.Recovered), g => g.Currency, g => g.Cents),
                AtRisk: SumByCurrency(groups.Where(g => g.Status == RecoveryStatus.ActiveRecovery), g => g.Currency, g => g.Cents),
                Reversed: SumByCurrency(groups, g => g.Currency, g => g.Reversed),
                FeesBilled: SumByCurrency(groups, g => g.Currency, g => g.Fee),
                LastFailureAtUtc: times?.LastFailed,
                LastRecoveryAtUtc: times?.LastRecovered,
                LatestScan: latestScan.GetValueOrDefault(m.Id));
        }).ToList();

        var totals = new AdminTotals(
            Merchants: merchants.Count,
            Connected: merchants.Count(m => m.Connected),
            Disconnected: merchants.Count(m => m.DisconnectedAtUtc != null),
            SignupsInWindow: merchants.Count(m => m.CreatedAt >= since),
            Backtests: backtests.Count,
            BacktestsComplete: backtests.Count(b => b.Status == nameof(BacktestStatus.Complete)),
            AccountsScanned: latestScan.Count,
            FailedAmountScanned: SumByCurrency(latestScan.Values, b => b.Currency, b => b.FailedAmountCents),
            ActiveCases: CountWith(caseGroups, RecoveryStatus.ActiveRecovery),
            RecoveredCases: CountWith(caseGroups, RecoveryStatus.Recovered),
            LostCases: CountWith(caseGroups, RecoveryStatus.Lost),
            CancelledCases: CountWith(caseGroups, RecoveryStatus.Cancelled),
            Recovered: SumByCurrency(caseGroups.Where(g => g.Status == RecoveryStatus.Recovered), g => g.Currency, g => g.Cents),
            RecoveredInWindow: SumByCurrency(recoveredInWindow, r => r.Currency, r => r.AmountCents),
            Reversed: SumByCurrency(caseGroups, g => g.Currency, g => g.Reversed),
            AtRisk: SumByCurrency(caseGroups.Where(g => g.Status == RecoveryStatus.ActiveRecovery), g => g.Currency, g => g.Cents),
            FeesInvoiced: feesInvoiced,
            WebhookEvents: webhookTotal,
            LastWebhookAtUtc: lastWebhook);

        var windowDates = Enumerable.Range(0, (now.Date - since.Date).Days + 1)
            .Select(i => since.Date.AddDays(i))
            .ToList();

        var signupsByDate = merchantRows.Where(m => m.CreatedAt >= since).ToLookup(m => m.CreatedAt.Date);
        var failedByDate = failedInWindow.ToLookup(d => d.Date);
        var recoveredByDate = recoveredInWindow.ToLookup(r => r.At.Date);
        var webhooksByDate = webhookWindow.ToLookup(w => w.ProcessedAt.Date);

        return new AdminStats(
            GeneratedAtUtc: now,
            WindowDays: days,
            Totals: totals,
            SignupsByDay: windowDates.Select(d => new DayCount(Day(d), signupsByDate[d].Count())).ToList(),
            ActivityByDay: windowDates.Select(d => new DayActivity(Day(d), failedByDate[d].Count(), recoveredByDate[d].Count())).ToList(),
            RecoveryCases: caseGroups
                .GroupBy(g => new { g.Status, g.Currency })
                .Select(g => new StatusMoney(g.Key.Status.ToString(), g.Key.Currency, g.Sum(x => x.Count), g.Sum(x => x.Cents)))
                .ToList(),
            Merchants: merchants,
            Backtests: backtests,
            Cases: cases,
            Declines: declines,
            Methods: methods,
            Retries: retries,
            Emails: emails,
            FeeInvoices: feeInvoices,
            Webhooks: new WebhookStats(
                webhookTotal,
                lastWebhook,
                webhookWindow.GroupBy(w => w.EventType)
                    .Select(g => new WebhookTypeCount(g.Key, g.Count()))
                    .OrderByDescending(t => t.Count)
                    .ToList(),
                windowDates.Select(d => new DayCount(Day(d), webhooksByDate[d].Count())).ToList()));
    }

    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd");

    /// <summary>One (merchant, status, currency) cell of the cases table, summed in SQL.</summary>
    private sealed record CaseGroup(
        Guid MerchantId, RecoveryStatus Status, string Currency, int Count, long Cents, long Reversed, long Fee);

    private static int CountWith(IEnumerable<CaseGroup> groups, RecoveryStatus status) =>
        groups.Where(g => g.Status == status).Sum(g => g.Count);

    /// <summary>One entry per currency, zeros dropped: an empty list reads as "nothing yet".</summary>
    private static IReadOnlyList<Money> SumByCurrency<T>(
        IEnumerable<T> rows, Func<T, string> currency, Func<T, long> cents) =>
        rows.GroupBy(currency)
            .Select(g => new Money(g.Key, g.Sum(cents)))
            .Where(m => m.Cents != 0)
            .OrderByDescending(m => m.Cents)
            .ToList();
}
