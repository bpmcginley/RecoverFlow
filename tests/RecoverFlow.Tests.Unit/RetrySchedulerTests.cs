using RecoverFlow.Domain.Services;

namespace RecoverFlow.Tests.Unit;

public class RetrySchedulerTests
{
    private static readonly DateTime FailedAt = new(2026, 7, 10, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Default_schedule_backs_off_6h_2d_5d_then_stops()
    {
        Assert.Equal(FailedAt.AddHours(6), RetryScheduler.CalculateNextRetry("generic_decline", 1, FailedAt));
        Assert.Equal(FailedAt.AddDays(2), RetryScheduler.CalculateNextRetry("generic_decline", 2, FailedAt));
        Assert.Equal(FailedAt.AddDays(5), RetryScheduler.CalculateNextRetry("generic_decline", 3, FailedAt));
        Assert.Equal(DateTime.MaxValue, RetryScheduler.CalculateNextRetry("generic_decline", 4, FailedAt));
    }

    [Fact]
    public void Processing_error_retries_quickly()
    {
        Assert.Equal(FailedAt.AddHours(2), RetryScheduler.CalculateNextRetry("processing_error", 1, FailedAt));
    }

    [Theory]
    [InlineData("expired_card")]
    [InlineData("incorrect_cvc")]
    public void Card_problems_go_to_card_update_not_retry(string code) =>
        Assert.Equal(DateTime.MaxValue, RetryScheduler.CalculateNextRetry(code, 1, FailedAt));

    [Fact]
    public void Insufficient_funds_waits_for_payday_morning()
    {
        // Failed on the 10th → next payday is the 15th, retry at 10:00
        var retry = RetryScheduler.CalculateNextRetry("insufficient_funds", 1, FailedAt);
        Assert.Equal(new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc), retry);
    }

    [Fact]
    public void Insufficient_funds_after_the_15th_waits_for_the_1st()
    {
        var failedLate = new DateTime(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc);
        var retry = RetryScheduler.CalculateNextRetry("insufficient_funds", 1, failedLate);
        Assert.Equal(new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc), retry);
    }

    [Fact]
    public void Scheduled_retries_preserve_utc_kind_for_postgres()
    {
        var retry = RetryScheduler.CalculateNextRetry("insufficient_funds", 1, FailedAt);
        Assert.Equal(DateTimeKind.Utc, retry.Kind);
    }
}
