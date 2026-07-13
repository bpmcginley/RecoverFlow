using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;

namespace RecoverFlow.Application.Backtest;

/// <summary>
/// Scans a newly-connected merchant's recent Stripe history so we can show them, on the spot,
/// what involuntary churn cost them and what RecoverFlow would have won back. Runs as a
/// background job (no tenant scope), so every query filters by merchant id explicitly.
/// </summary>
public sealed class AccountBacktestService(
    IAppDbContext db,
    IHistoricalInvoiceReader invoices,
    IOptions<BacktestOptions> backtestOptions,
    IOptions<BillingOptions> billingOptions,
    ILogger<AccountBacktestService> log)
{
    private readonly BacktestOptions _opts = backtestOptions.Value;
    private readonly int _feeBps = billingOptions.Value.FeeBasisPoints;

    /// <summary>Creates a Pending row in the request path so the page has something to poll immediately.</summary>
    public async Task<Guid> BeginAsync(Guid merchantId, CancellationToken ct = default)
    {
        var backtest = new AccountBacktest
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            Status = BacktestStatus.Pending,
            WindowDays = _opts.WindowDays,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.AccountBacktests.Add(backtest);
        await db.SaveChangesAsync(ct);
        return backtest.Id;
    }

    /// <summary>The background-job body: fills in a Pending backtest, or marks it Failed.</summary>
    public async Task RunAsync(Guid backtestId, CancellationToken ct = default)
    {
        var backtest = await db.AccountBacktests
            .FirstOrDefaultAsync(b => b.Id == backtestId, ct);
        if (backtest is null)
        {
            log.LogWarning("Backtest {BacktestId} vanished before it could run", backtestId);
            return;
        }
        if (backtest.Status != BacktestStatus.Pending) return; // Already ran (retry/no-op)

        var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.Id == backtest.MerchantId, ct);
        if (merchant is null)
        {
            await FailAsync(backtest, "Merchant not found.", ct);
            return;
        }

        try
        {
            var since = DateTime.UtcNow.AddDays(-_opts.WindowDays);
            var found = await invoices.ListUncollectedInvoicesAsync(
                merchant.StripeAccountId, since, _opts.MaxInvoices, ct);

            Populate(backtest, found);
            backtest.Status = BacktestStatus.Complete;
            backtest.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            log.LogInformation(
                "Backtest {BacktestId} for merchant {MerchantId}: {Count} lost invoices, {Amount} {Currency}",
                backtest.Id, merchant.Id, backtest.FailedInvoiceCount, backtest.FailedAmountCents, backtest.Currency);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Backtest {BacktestId} failed", backtest.Id);
            await FailAsync(backtest, ex.Message, ct);
        }
    }

    // Focus the reported figures on the account's dominant currency — mixing currencies into one
    // total would be meaningless. Other-currency invoices are simply left out of v1's numbers.
    private void Populate(AccountBacktest backtest, IReadOnlyList<HistoricalFailedInvoice> found)
    {
        if (found.Count == 0)
        {
            backtest.Currency = "usd";
            backtest.BreakdownJson = "[]";
            return;
        }

        var currency = found
            .GroupBy(i => i.Currency.ToLowerInvariant())
            .OrderByDescending(g => g.Sum(i => i.AmountCents))
            .First().Key;

        var scoped = found.Where(i => i.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase)).ToList();

        backtest.Currency = currency;
        backtest.FailedInvoiceCount = scoped.Count;
        backtest.FailedAmountCents = scoped.Sum(i => i.AmountCents);

        var breakdown = scoped
            .GroupBy(i => BacktestRecoveryModel.Classify(i.DeclineCode))
            .Select(g => BuildRow(g.Key, g.ToList()))
            .OrderByDescending(r => r.AmountCents)
            .ToList();

        backtest.RecoverableLowCents = breakdown.Sum(r => r.RecoverableLowCents);
        backtest.RecoverableHighCents = breakdown.Sum(r => r.RecoverableHighCents);
        backtest.EstimatedFeeLowCents = backtest.RecoverableLowCents * _feeBps / 10_000;
        backtest.EstimatedFeeHighCents = backtest.RecoverableHighCents * _feeBps / 10_000;
        backtest.BreakdownJson = JsonSerializer.Serialize(breakdown);
    }

    private static BacktestBreakdownRow BuildRow(DeclineType type, List<HistoricalFailedInvoice> group)
    {
        var (low, high) = BacktestRecoveryModel.RecoveryRange(type);
        var amount = group.Sum(i => i.AmountCents);
        return new BacktestBreakdownRow(
            type.ToString(),
            group.Count,
            amount,
            (long)(amount * low),
            (long)(amount * high));
    }

    private async Task FailAsync(AccountBacktest backtest, string reason, CancellationToken ct)
    {
        backtest.Status = BacktestStatus.Failed;
        backtest.FailureReason = reason.Length > 1000 ? reason[..1000] : reason;
        backtest.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public static BacktestView ToView(AccountBacktest b) => new(
        b.Status.ToString(),
        b.WindowDays,
        b.FailedInvoiceCount,
        b.FailedAmountCents,
        b.Currency,
        b.RecoverableLowCents,
        b.RecoverableHighCents,
        b.EstimatedFeeLowCents,
        b.EstimatedFeeHighCents,
        JsonSerializer.Deserialize<List<BacktestBreakdownRow>>(b.BreakdownJson) ?? []);
}
