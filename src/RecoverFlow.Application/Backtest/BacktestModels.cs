namespace RecoverFlow.Application.Backtest;

/// <summary>One row of the per-decline-type breakdown, persisted as JSON on the backtest.</summary>
public sealed record BacktestBreakdownRow(
    string DeclineType,
    int Count,
    long AmountCents,
    long RecoverableLowCents,
    long RecoverableHighCents);

/// <summary>Shape returned to the connected page while and after the scan runs.</summary>
public sealed record BacktestView(
    string Status,
    int WindowDays,
    int FailedInvoiceCount,
    long FailedAmountCents,
    string Currency,
    long RecoverableLowCents,
    long RecoverableHighCents,
    long EstimatedFeeLowCents,
    long EstimatedFeeHighCents,
    IReadOnlyList<BacktestBreakdownRow> Breakdown);

/// <summary>Enqueues the backtest scan to run out-of-band right after a merchant connects.</summary>
public interface IBacktestJobScheduler
{
    void Enqueue(Guid backtestId);
}
