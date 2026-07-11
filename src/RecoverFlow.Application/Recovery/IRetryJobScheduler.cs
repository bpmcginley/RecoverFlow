namespace RecoverFlow.Application.Recovery;

/// <summary>
/// Schedules a <see cref="RetryExecutionService"/> run for a persisted retry attempt.
/// Keeps the background-job engine (Hangfire) out of the Application layer.
/// </summary>
public interface IRetryJobScheduler
{
    void ScheduleAttempt(Guid attemptId, DateTime runAtUtc);
}
