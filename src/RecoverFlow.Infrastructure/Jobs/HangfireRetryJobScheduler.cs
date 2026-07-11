using Hangfire;
using RecoverFlow.Application.Recovery;

namespace RecoverFlow.Infrastructure.Jobs;

public sealed class HangfireRetryJobScheduler(IBackgroundJobClient jobs) : IRetryJobScheduler
{
    public void ScheduleAttempt(Guid attemptId, DateTime runAtUtc) =>
        jobs.Schedule<RetryExecutionService>(
            s => s.ExecuteAsync(attemptId, CancellationToken.None),
            new DateTimeOffset(DateTime.SpecifyKind(runAtUtc, DateTimeKind.Utc)));
}
