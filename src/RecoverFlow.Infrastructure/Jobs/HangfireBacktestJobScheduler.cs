using Hangfire;
using RecoverFlow.Application.Backtest;

namespace RecoverFlow.Infrastructure.Jobs;

public sealed class HangfireBacktestJobScheduler(IBackgroundJobClient jobs) : IBacktestJobScheduler
{
    public void Enqueue(Guid backtestId) =>
        jobs.Enqueue<AccountBacktestService>(s => s.RunAsync(backtestId, CancellationToken.None));
}
