using RecoverFlow.Application.Dashboard;
using RecoverFlow.Domain;

namespace RecoverFlow.Tests.Unit;

public class MerchantDashboardStatsTests
{
    private static readonly DateTime Now = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Cutoff = Now.AddDays(-30);

    private static CaseSnapshot Case(
        RecoveryStatus status, long amount = 1000,
        DateTime? openedAt = null, DateTime? recoveredAt = null, DateTime? lostAt = null) =>
        new(status, amount, openedAt ?? Now.AddDays(-1), recoveredAt, lostAt);

    [Fact]
    public void No_cases_yields_zero_rate_not_divide_by_zero()
    {
        var stats = MerchantDashboardService.ComputeStats([], since: null);

        Assert.Equal(0, stats.RecoveryRate);
        Assert.Equal(0, stats.AmountRecoveredCents);
        Assert.Equal(0, stats.AmountAtRiskCents);
        Assert.All(stats.CasesByStatus.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Only_open_cases_yields_zero_rate()
    {
        var stats = MerchantDashboardService.ComputeStats(
            [Case(RecoveryStatus.ActiveRecovery), Case(RecoveryStatus.Cancelled)], since: null);

        Assert.Equal(0, stats.RecoveryRate);
    }

    [Fact]
    public void Rate_is_recovered_over_recovered_plus_lost()
    {
        var stats = MerchantDashboardService.ComputeStats(
        [
            Case(RecoveryStatus.Recovered, recoveredAt: Now.AddDays(-2)),
            Case(RecoveryStatus.Recovered, recoveredAt: Now.AddDays(-3)),
            Case(RecoveryStatus.Recovered, recoveredAt: Now.AddDays(-4)),
            Case(RecoveryStatus.Lost, lostAt: Now.AddDays(-5)),
            Case(RecoveryStatus.ActiveRecovery), // open cases don't dilute the rate
            Case(RecoveryStatus.Cancelled),
        ], since: null);

        Assert.Equal(0.75, stats.RecoveryRate);
    }

    [Fact]
    public void All_recovered_yields_rate_of_one()
    {
        var stats = MerchantDashboardService.ComputeStats(
            [Case(RecoveryStatus.Recovered, recoveredAt: Now.AddDays(-1))], since: null);

        Assert.Equal(1, stats.RecoveryRate);
    }

    [Fact]
    public void Amounts_split_recovered_vs_at_risk()
    {
        var stats = MerchantDashboardService.ComputeStats(
        [
            Case(RecoveryStatus.Recovered, amount: 2500, recoveredAt: Now.AddDays(-1)),
            Case(RecoveryStatus.Recovered, amount: 1500, recoveredAt: Now.AddDays(-2)),
            Case(RecoveryStatus.ActiveRecovery, amount: 9900),
            Case(RecoveryStatus.Lost, amount: 5000, lostAt: Now.AddDays(-3)), // neither bucket
        ], since: null);

        Assert.Equal(4000, stats.AmountRecoveredCents);
        Assert.Equal(9900, stats.AmountAtRiskCents);
    }

    [Fact]
    public void Counts_cover_every_status_including_zeroes()
    {
        var stats = MerchantDashboardService.ComputeStats(
            [Case(RecoveryStatus.ActiveRecovery), Case(RecoveryStatus.ActiveRecovery)], since: null);

        Assert.Equal(2, stats.CasesByStatus["ActiveRecovery"]);
        Assert.Equal(0, stats.CasesByStatus["Recovered"]);
        Assert.Equal(0, stats.CasesByStatus["Lost"]);
        Assert.Equal(0, stats.CasesByStatus["Cancelled"]);
    }

    [Fact]
    public void Window_includes_case_closed_inside_even_if_opened_before_it()
    {
        // Opened 60 days ago but recovered 5 days ago → counts for the last 30 days.
        var stats = MerchantDashboardService.ComputeStats(
            [Case(RecoveryStatus.Recovered, amount: 700, openedAt: Now.AddDays(-60), recoveredAt: Now.AddDays(-5))],
            since: Cutoff);

        Assert.Equal(1, stats.CasesByStatus["Recovered"]);
        Assert.Equal(700, stats.AmountRecoveredCents);
    }

    [Fact]
    public void Window_excludes_case_closed_before_it()
    {
        var old = Case(RecoveryStatus.Recovered, amount: 700, openedAt: Now.AddDays(-60), recoveredAt: Now.AddDays(-45));

        var allTime = MerchantDashboardService.ComputeStats([old], since: null);
        var last30 = MerchantDashboardService.ComputeStats([old], since: Cutoff);

        Assert.Equal(1, allTime.CasesByStatus["Recovered"]);
        Assert.Equal(0, last30.CasesByStatus["Recovered"]);
        Assert.Equal(0, last30.AmountRecoveredCents);
        Assert.Equal(0, last30.RecoveryRate);
    }

    [Fact]
    public void Window_keys_open_cases_on_opening_and_closed_cases_on_closure()
    {
        var stats = MerchantDashboardService.ComputeStats(
        [
            Case(RecoveryStatus.ActiveRecovery, amount: 100, openedAt: Now.AddDays(-40)),        // opened outside → out
            Case(RecoveryStatus.ActiveRecovery, amount: 200, openedAt: Now.AddDays(-10)),        // opened inside → in
            Case(RecoveryStatus.Lost, openedAt: Now.AddDays(-40), lostAt: Now.AddDays(-35)),     // lost outside → out
            Case(RecoveryStatus.Lost, openedAt: Now.AddDays(-40), lostAt: Now.AddDays(-2)),      // lost inside → in
        ], since: Cutoff);

        Assert.Equal(1, stats.CasesByStatus["ActiveRecovery"]);
        Assert.Equal(200, stats.AmountAtRiskCents);
        Assert.Equal(1, stats.CasesByStatus["Lost"]);
    }

    [Fact]
    public void Windowed_rate_uses_only_cases_closed_in_the_window()
    {
        var stats = MerchantDashboardService.ComputeStats(
        [
            Case(RecoveryStatus.Recovered, recoveredAt: Now.AddDays(-45)), // outside
            Case(RecoveryStatus.Recovered, recoveredAt: Now.AddDays(-5)),  // inside
            Case(RecoveryStatus.Lost, lostAt: Now.AddDays(-3)),            // inside
        ], since: Cutoff);

        Assert.Equal(0.5, stats.RecoveryRate);
    }

    [Fact]
    public void Boundary_moment_exactly_at_cutoff_is_included()
    {
        var stats = MerchantDashboardService.ComputeStats(
            [Case(RecoveryStatus.Recovered, recoveredAt: Cutoff)], since: Cutoff);

        Assert.Equal(1, stats.CasesByStatus["Recovered"]);
    }
}
