namespace RecoverFlow.Domain.Services;

public static class RetryScheduler
{
    // Strategy: spread attempts across different times of day and week rather than
    // hammering the same slot, and aim insufficient_funds at the 1st and 15th, when
    // balances are most likely to have been topped up.
    //
    // These are reasoned starting points, not measured results. We have no recovery
    // data of our own yet, and the published "best time to retry" figures are vendor
    // sourced, so nothing here is tuned against a benchmark we would be willing to
    // print. Revisit once there is enough real outcome data to test it.

    /// <summary>Returns DateTime.MaxValue when no further retry should be scheduled.</summary>
    public static DateTime CalculateNextRetry(string? declineCode, int attemptNumber, DateTime failedAt) =>
        declineCode switch
        {
            "insufficient_funds" => GetNextPaydayRetry(failedAt, attemptNumber),
            "processing_error" => failedAt.AddHours(2), // Quick retry
            "expired_card" => DateTime.MaxValue,   // Don't retry, send card update email
            "incorrect_cvc" => DateTime.MaxValue,  // Don't retry, send card update email
            _ => GetDefaultRetrySchedule(failedAt, attemptNumber),
        };

    private static DateTime GetDefaultRetrySchedule(DateTime failedAt, int attempt) =>
        attempt switch
        {
            1 => failedAt.AddHours(6), // 6 hours later (different time of day)
            2 => failedAt.AddDays(2),  // 2 days later
            3 => failedAt.AddDays(5),  // 5 days later (likely next payday cycle)
            _ => DateTime.MaxValue,    // Stop retrying
        };

    private static DateTime GetNextPaydayRetry(DateTime failedAt, int attempt)
    {
        // Common paydays: 1st and 15th of month
        var today = failedAt.Date;
        var nextPayday = today.Day < 15
            ? new DateTime(today.Year, today.Month, 15, 0, 0, 0, today.Kind)
            : new DateTime(today.Year, today.Month, 1, 0, 0, 0, today.Kind).AddMonths(1);

        return attempt switch
        {
            1 => nextPayday.AddHours(10),            // Morning of payday
            2 => nextPayday.AddDays(1).AddHours(10), // Day after payday
            3 => nextPayday.AddDays(15).AddHours(10), // Next payday
            _ => DateTime.MaxValue,
        };
    }
}
