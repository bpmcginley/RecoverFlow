using Microsoft.EntityFrameworkCore;
using RecoverFlow.Application.Admin;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;

namespace RecoverFlow.Tests.Unit;

/// <summary>
/// The internal dashboard reads across every merchant. What it must never do is add
/// currencies, count a rerun scan twice, or credit one merchant with another's recoveries.
/// </summary>
public class AdminStatsServiceTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateTime Now = DateTime.UtcNow;

    private Merchant Merchant(string company, bool connected = true, DateTime? left = null)
    {
        var m = new Merchant
        {
            Id = Guid.NewGuid(),
            Email = company.ToLowerInvariant() + "@example.test",
            CompanyName = company,
            StripeAccountId = "acct_" + company,
            EncryptedStripeAccessToken = connected || left is not null ? "at" : null,
            DisconnectedAtUtc = left,
            CreatedAt = Now.AddDays(-10),
        };
        _db.Merchants.Add(m);
        return m;
    }

    private FailedPayment Case(Merchant m, RecoveryStatus status, long cents, string currency = "usd",
        DateTime? failedAt = null, long reversed = 0, long fee = 0, string? decline = "insufficient_funds")
    {
        var p = new FailedPayment
        {
            Id = Guid.NewGuid(),
            MerchantId = m.Id,
            StripeInvoiceId = "in_" + Guid.NewGuid().ToString("N")[..8],
            AmountCents = cents,
            Currency = currency,
            Status = status,
            DeclineCode = decline,
            FailureType = DeclineType.SoftDecline,
            RecoveryMethod = status == RecoveryStatus.Recovered ? RecoveryMethod.SmartRetry : RecoveryMethod.Unknown,
            FirstFailedAt = failedAt ?? Now.AddDays(-3),
            RecoveredAt = status == RecoveryStatus.Recovered ? (failedAt ?? Now.AddDays(-3)).AddDays(1) : null,
            ReversedAmountCents = reversed,
            BilledFeeCents = fee,
        };
        _db.FailedPayments.Add(p);
        return p;
    }

    private AdminStatsService Service() => new(_db);

    [Fact]
    public async Task Recovered_revenue_is_credited_to_the_merchant_it_belongs_to()
    {
        var acme = Merchant("Acme");
        var globex = Merchant("Globex");
        Case(acme, RecoveryStatus.Recovered, 10_000);
        Case(acme, RecoveryStatus.Recovered, 5_000);
        Case(globex, RecoveryStatus.Recovered, 700);
        Case(globex, RecoveryStatus.ActiveRecovery, 300);
        await _db.SaveChangesAsync();

        var stats = await Service().BuildAsync(30);

        var a = stats.Merchants.Single(m => m.CompanyName == "Acme");
        var g = stats.Merchants.Single(m => m.CompanyName == "Globex");
        Assert.Equal([new Money("usd", 15_000)], a.Recovered);
        Assert.Equal(2, a.RecoveredCases);
        Assert.Equal([new Money("usd", 700)], g.Recovered);
        Assert.Equal([new Money("usd", 300)], g.AtRisk);
        Assert.Equal(1, g.ActiveCases);
    }

    [Fact]
    public async Task Currencies_are_kept_apart_rather_than_added()
    {
        var m = Merchant("Acme");
        Case(m, RecoveryStatus.Recovered, 10_000, "usd");
        Case(m, RecoveryStatus.Recovered, 4_000, "eur");
        await _db.SaveChangesAsync();

        var stats = await Service().BuildAsync(30);

        Assert.Equal(2, stats.Totals.Recovered.Count);
        Assert.Equal(10_000, stats.Totals.Recovered.Single(x => x.Currency == "usd").Cents);
        Assert.Equal(4_000, stats.Totals.Recovered.Single(x => x.Currency == "eur").Cents);
        Assert.Equal(2, stats.Merchants.Single().Recovered.Count);
    }

    [Fact]
    public async Task Reversals_and_billed_fees_sit_beside_the_recovery_not_inside_it()
    {
        var m = Merchant("Acme");
        Case(m, RecoveryStatus.Recovered, 10_000, reversed: 2_500, fee: 1_875);
        await _db.SaveChangesAsync();

        var stats = await Service().BuildAsync(30);

        var row = stats.Merchants.Single();
        Assert.Equal([new Money("usd", 10_000)], row.Recovered);
        Assert.Equal([new Money("usd", 2_500)], row.Reversed);
        Assert.Equal([new Money("usd", 1_875)], row.FeesBilled);
        Assert.Equal([new Money("usd", 2_500)], stats.Totals.Reversed);
    }

    [Fact]
    public async Task A_rerun_scan_counts_once_and_the_newest_complete_run_wins()
    {
        var m = Merchant("Acme");
        _db.AccountBacktests.AddRange(
            new AccountBacktest { Id = Guid.NewGuid(), MerchantId = m.Id, Status = BacktestStatus.Complete, FailedAmountCents = 1_000, CreatedAtUtc = Now.AddDays(-2) },
            new AccountBacktest { Id = Guid.NewGuid(), MerchantId = m.Id, Status = BacktestStatus.Complete, FailedAmountCents = 3_000, CreatedAtUtc = Now.AddDays(-1) },
            new AccountBacktest { Id = Guid.NewGuid(), MerchantId = m.Id, Status = BacktestStatus.Failed, FailedAmountCents = 0, CreatedAtUtc = Now });
        await _db.SaveChangesAsync();

        var stats = await Service().BuildAsync(30);

        Assert.Equal(3, stats.Totals.Backtests);
        Assert.Equal(1, stats.Totals.AccountsScanned);
        Assert.Equal([new Money("usd", 3_000)], stats.Totals.FailedAmountScanned);
        Assert.Equal(3_000, stats.Merchants.Single().LatestScan!.FailedAmountCents);
        Assert.All(stats.Backtests, b => Assert.Equal("Acme", b.CompanyName));
    }

    [Fact]
    public async Task Activity_by_day_covers_every_day_in_the_window_and_only_that_window()
    {
        var m = Merchant("Acme");
        Case(m, RecoveryStatus.Recovered, 1_000, failedAt: Now.AddDays(-2));
        Case(m, RecoveryStatus.Lost, 1_000, failedAt: Now.AddDays(-2));
        Case(m, RecoveryStatus.Lost, 1_000, failedAt: Now.AddDays(-40)); // outside a 7-day window
        await _db.SaveChangesAsync();

        var stats = await Service().BuildAsync(7);

        Assert.Equal(8, stats.ActivityByDay.Count);
        Assert.Equal(2, stats.ActivityByDay.Sum(d => d.Failed));
        Assert.Equal(1, stats.ActivityByDay.Sum(d => d.Recovered));
        Assert.Equal([new Money("usd", 1_000)], stats.Totals.RecoveredInWindow);
        Assert.Equal(2, stats.Totals.LostCases);
    }

    [Fact]
    public async Task Connected_means_installed_and_not_since_gone()
    {
        Merchant("Live");
        Merchant("Never", connected: false);
        Merchant("Gone", left: Now.AddDays(-1));
        await _db.SaveChangesAsync();

        var stats = await Service().BuildAsync(30);

        Assert.Equal(3, stats.Totals.Merchants);
        Assert.Equal(1, stats.Totals.Connected);
        Assert.Equal(1, stats.Totals.Disconnected);
    }

    [Fact]
    public async Task Fee_invoices_name_the_merchant_and_only_sent_ones_count_as_invoiced()
    {
        var m = Merchant("Acme");
        _db.FeeInvoices.AddRange(
            new FeeInvoice { Id = Guid.NewGuid(), MerchantId = m.Id, PeriodLabel = "2026-08", TotalCents = 5_000, Status = FeeInvoiceStatus.Sent, CreatedAtUtc = Now.AddDays(-1) },
            new FeeInvoice { Id = Guid.NewGuid(), MerchantId = m.Id, PeriodLabel = "2026-09", TotalCents = 9_000, Status = FeeInvoiceStatus.Failed, CreatedAtUtc = Now });
        await _db.SaveChangesAsync();

        var stats = await Service().BuildAsync(30);

        Assert.Equal([new Money("usd", 5_000)], stats.Totals.FeesInvoiced);
        Assert.Equal(2, stats.FeeInvoices.Count);
        Assert.All(stats.FeeInvoices, f => Assert.Equal("Acme", f.CompanyName));
        Assert.Equal("2026-09", stats.FeeInvoices[0].PeriodLabel);
    }

    [Fact]
    public async Task Webhook_ledger_reports_the_last_event_and_the_types_in_the_window()
    {
        _db.ProcessedWebhookEvents.AddRange(
            new ProcessedWebhookEvent { EventId = "evt_1", EventType = "invoice.payment_failed", ProcessedAt = Now.AddDays(-1) },
            new ProcessedWebhookEvent { EventId = "evt_2", EventType = "invoice.payment_failed", ProcessedAt = Now.AddHours(-1) },
            new ProcessedWebhookEvent { EventId = "evt_3", EventType = "invoice.paid", ProcessedAt = Now.AddDays(-60) });
        await _db.SaveChangesAsync();

        var stats = await Service().BuildAsync(30);

        Assert.Equal(3, stats.Webhooks.Total);
        Assert.Equal(Now.AddHours(-1), stats.Webhooks.LastAtUtc);
        Assert.Equal([new WebhookTypeCount("invoice.payment_failed", 2)], stats.Webhooks.ByTypeInWindow);
    }

    [Fact]
    public async Task Enums_reach_the_page_as_names_not_numbers()
    {
        var m = Merchant("Acme");
        Case(m, RecoveryStatus.Recovered, 1_000);
        await _db.SaveChangesAsync();

        var stats = await Service().BuildAsync(30);

        Assert.Equal("Recovered", stats.Cases.Single().Status);
        Assert.Equal("SmartRetry", stats.Cases.Single().Method);
        Assert.Equal("Recovered", stats.RecoveryCases.Single().Status);
        Assert.Equal([new MethodBreakdown("SmartRetry", 1)], stats.Methods);
        Assert.Equal("Acme", stats.Cases.Single().CompanyName);
    }
}
