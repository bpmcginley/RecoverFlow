using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Backtest;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;

namespace RecoverFlow.Tests.Unit;

public class AccountBacktestServiceTests
{
    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static Merchant SeedMerchant(AppDbContext db)
    {
        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            Email = "owner@acme.test",
            CompanyName = "Acme Corp",
            StripeAccountId = $"acct_{Guid.NewGuid():N}",
            EncryptedStripeAccessToken = "encrypted-token",
            CreatedAt = DateTime.UtcNow,
        };
        db.Merchants.Add(merchant);
        db.SaveChanges();
        return merchant;
    }

    private static AccountBacktestService Service(AppDbContext db, FakeHistoricalInvoiceReader reader) =>
        new(db, reader,
            Options.Create(new BacktestOptions { WindowDays = 90, MaxInvoices = 200 }),
            Options.Create(new BillingOptions { FeeBasisPoints = 2500 }),
            NullLogger<AccountBacktestService>.Instance);

    private static HistoricalFailedInvoice Inv(long cents, string? decline = null, string currency = "usd") =>
        new($"in_{Guid.NewGuid():N}", cents, currency, decline);

    [Fact]
    public async Task Begin_creates_a_pending_row()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db);

        var id = await Service(db, new FakeHistoricalInvoiceReader()).BeginAsync(merchant.Id);

        var row = await db.AccountBacktests.SingleAsync();
        Assert.Equal(id, row.Id);
        Assert.Equal(BacktestStatus.Pending, row.Status);
        Assert.Equal(90, row.WindowDays);
    }

    [Fact]
    public async Task Lost_dollars_are_summed_exactly_from_stripe()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db);
        var reader = new FakeHistoricalInvoiceReader();
        reader.Invoices.AddRange([Inv(10_000), Inv(5_000), Inv(2_500)]);
        var svc = Service(db, reader);
        var id = await svc.BeginAsync(merchant.Id);

        await svc.RunAsync(id);

        var row = await db.AccountBacktests.SingleAsync();
        Assert.Equal(BacktestStatus.Complete, row.Status);
        Assert.Equal(3, row.FailedInvoiceCount);
        Assert.Equal(17_500, row.FailedAmountCents);
    }

    [Fact]
    public async Task Soft_declines_use_the_30_to_50_percent_range_and_fee_is_25_percent_of_that()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db);
        var reader = new FakeHistoricalInvoiceReader();
        reader.Invoices.Add(Inv(10_000, "insufficient_funds")); // soft
        var svc = Service(db, reader);
        var id = await svc.BeginAsync(merchant.Id);

        await svc.RunAsync(id);

        var row = await db.AccountBacktests.SingleAsync();
        Assert.Equal(3_000, row.RecoverableLowCents);   // 30%
        Assert.Equal(5_000, row.RecoverableHighCents);  // 50%
        Assert.Equal(750, row.EstimatedFeeLowCents);    // 25% of 3000
        Assert.Equal(1_250, row.EstimatedFeeHighCents); // 25% of 5000
    }

    [Fact]
    public async Task Hard_declines_are_scored_as_all_but_unrecoverable()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db);
        var reader = new FakeHistoricalInvoiceReader();
        reader.Invoices.Add(Inv(10_000, "stolen_card")); // hard: 0–5%
        var svc = Service(db, reader);
        var id = await svc.BeginAsync(merchant.Id);

        await svc.RunAsync(id);

        var row = await db.AccountBacktests.SingleAsync();
        Assert.Equal(0, row.RecoverableLowCents);
        Assert.Equal(500, row.RecoverableHighCents); // 5%
    }

    [Fact]
    public async Task Null_decline_falls_into_the_conservative_unknown_bucket()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db);
        var reader = new FakeHistoricalInvoiceReader();
        reader.Invoices.Add(Inv(10_000, decline: null)); // unknown: 15–30%
        var svc = Service(db, reader);
        var id = await svc.BeginAsync(merchant.Id);

        await svc.RunAsync(id);

        var row = await db.AccountBacktests.SingleAsync();
        Assert.Equal(1_500, row.RecoverableLowCents);
        Assert.Equal(3_000, row.RecoverableHighCents);
    }

    [Fact]
    public async Task Breakdown_groups_by_decline_type()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db);
        var reader = new FakeHistoricalInvoiceReader();
        reader.Invoices.AddRange([
            Inv(10_000, "insufficient_funds"), // soft
            Inv(4_000, "card_declined"),       // soft
            Inv(6_000, "stolen_card"),         // hard
        ]);
        var svc = Service(db, reader);
        var id = await svc.BeginAsync(merchant.Id);

        await svc.RunAsync(id);

        var view = AccountBacktestService.ToView(await db.AccountBacktests.SingleAsync());
        var soft = view.Breakdown.Single(r => r.DeclineType == "SoftDecline");
        var hard = view.Breakdown.Single(r => r.DeclineType == "HardDecline");
        Assert.Equal(2, soft.Count);
        Assert.Equal(14_000, soft.AmountCents);
        Assert.Equal(1, hard.Count);
        Assert.Equal(6_000, hard.AmountCents);
    }

    [Fact]
    public async Task Only_the_dominant_currency_is_reported()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db);
        var reader = new FakeHistoricalInvoiceReader();
        reader.Invoices.AddRange([
            Inv(10_000, "insufficient_funds", "usd"),
            Inv(9_000, "insufficient_funds", "usd"), // usd total 19_000
            Inv(50_000, "insufficient_funds", "eur"), // eur total 50_000 dominates
        ]);
        var svc = Service(db, reader);
        var id = await svc.BeginAsync(merchant.Id);

        await svc.RunAsync(id);

        var row = await db.AccountBacktests.SingleAsync();
        Assert.Equal("eur", row.Currency);
        Assert.Equal(1, row.FailedInvoiceCount);
        Assert.Equal(50_000, row.FailedAmountCents);
    }

    [Fact]
    public async Task Empty_history_completes_cleanly_with_zeroes()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db);
        var svc = Service(db, new FakeHistoricalInvoiceReader());
        var id = await svc.BeginAsync(merchant.Id);

        await svc.RunAsync(id);

        var row = await db.AccountBacktests.SingleAsync();
        Assert.Equal(BacktestStatus.Complete, row.Status);
        Assert.Equal(0, row.FailedInvoiceCount);
        Assert.Equal(0, row.FailedAmountCents);
        Assert.Equal("[]", row.BreakdownJson);
    }

    [Fact]
    public async Task Stripe_failure_marks_the_backtest_failed_not_crashed()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db);
        var reader = new FakeHistoricalInvoiceReader { Throw = new InvalidOperationException("stripe down") };
        var svc = Service(db, reader);
        var id = await svc.BeginAsync(merchant.Id);

        await svc.RunAsync(id);

        var row = await db.AccountBacktests.SingleAsync();
        Assert.Equal(BacktestStatus.Failed, row.Status);
        Assert.Equal("stripe down", row.FailureReason);
    }

    [Fact]
    public async Task Run_is_idempotent_once_complete()
    {
        using var db = CreateDb();
        var merchant = SeedMerchant(db);
        var reader = new FakeHistoricalInvoiceReader();
        reader.Invoices.Add(Inv(10_000, "insufficient_funds"));
        var svc = Service(db, reader);
        var id = await svc.BeginAsync(merchant.Id);
        await svc.RunAsync(id);

        // A second run (e.g. Hangfire retry) must not re-scan or double up.
        await svc.RunAsync(id);

        Assert.Single(reader.Calls);
    }
}
