using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RecoverFlow.Application.Dashboard;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;

namespace RecoverFlow.Tests.Unit;

/// <summary>
/// The projections behind the embedded Stripe app: per-customer case lists, invoice
/// lookup, dunning progress, and the status filter on the paged case list.
/// </summary>
public sealed class AppDashboardProjectionTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly Guid _merchantA = Guid.NewGuid();
    private readonly Guid _merchantB = Guid.NewGuid();

    public AppDashboardProjectionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        db.Merchants.AddRange(NewMerchant(_merchantA, "a"), NewMerchant(_merchantB, "b"));
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private MerchantDashboardService ServiceOver(AppDbContext db) => new(db);

    private static Merchant NewMerchant(Guid id, string tag) => new()
    {
        Id = id,
        Email = $"{tag}@example.com",
        CompanyName = tag,
        StripeAccountId = $"acct_{tag}",
        CreatedAt = Now,
    };

    private static FailedPayment NewCase(
        Guid merchantId, string invoiceId, string? customerId = null,
        RecoveryStatus status = RecoveryStatus.ActiveRecovery, DateTime? failedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        MerchantId = merchantId,
        StripeInvoiceId = invoiceId,
        StripeCustomerId = customerId,
        CustomerEmail = "customer@example.com",
        Currency = "usd",
        AmountCents = 2500,
        Status = status,
        FirstFailedAt = failedAt ?? Now,
    };

    [Fact]
    public async Task Customer_cases_come_newest_first()
    {
        await using (var db = CreateContext())
        {
            db.FailedPayments.AddRange(
                NewCase(_merchantA, "in_old", "cus_1", failedAt: Now.AddDays(-3)),
                NewCase(_merchantA, "in_new", "cus_1", failedAt: Now.AddDays(-1)),
                NewCase(_merchantA, "in_mid", "cus_1", failedAt: Now.AddDays(-2)),
                NewCase(_merchantA, "in_other_customer", "cus_2"));
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var list = await ServiceOver(read).GetCustomerCasesAsync(_merchantA, "cus_1");

        Assert.Equal(3, list.TotalCount);
        Assert.Equal(["in_new", "in_mid", "in_old"], list.Items.Select(i => i.StripeInvoiceId));
    }

    [Fact]
    public async Task Customer_cases_are_capped_but_report_the_full_total()
    {
        await using (var db = CreateContext())
        {
            for (var i = 0; i < MerchantDashboardService.MaxCustomerCases + 1; i++)
                db.FailedPayments.Add(NewCase(_merchantA, $"in_{i}", "cus_many", failedAt: Now.AddHours(-i)));
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var list = await ServiceOver(read).GetCustomerCasesAsync(_merchantA, "cus_many");

        Assert.Equal(MerchantDashboardService.MaxCustomerCases, list.Items.Count);
        Assert.Equal(MerchantDashboardService.MaxCustomerCases + 1, list.TotalCount);
        Assert.Equal("in_0", list.Items[0].StripeInvoiceId); // newest survives the cap
    }

    [Fact]
    public async Task Customer_with_no_activity_yields_an_empty_200_shape()
    {
        await using var db = CreateContext();
        var list = await ServiceOver(db).GetCustomerCasesAsync(_merchantA, "cus_quiet");

        Assert.Empty(list.Items);
        Assert.Equal(0, list.TotalCount);
    }

    [Fact]
    public async Task Customer_cases_never_cross_merchants()
    {
        await using (var db = CreateContext())
        {
            // Same Stripe customer id under both merchants; only A's row may show for A.
            db.FailedPayments.AddRange(
                NewCase(_merchantA, "in_a", "cus_shared"),
                NewCase(_merchantB, "in_b", "cus_shared"));
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var list = await ServiceOver(read).GetCustomerCasesAsync(_merchantA, "cus_shared");

        Assert.Equal("in_a", Assert.Single(list.Items).StripeInvoiceId);
        Assert.Equal(1, list.TotalCount);
    }

    [Fact]
    public async Task Invoice_lookup_returns_the_case_with_attempts_counted()
    {
        await using (var db = CreateContext())
        {
            var c = NewCase(_merchantA, "in_target", "cus_1", RecoveryStatus.Recovered);
            c.RecoveredAt = Now.AddDays(1);
            c.RetryAttempts =
            [
                new RetryAttempt { Id = Guid.NewGuid(), AttemptNumber = 1, ScheduledFor = Now },
                new RetryAttempt { Id = Guid.NewGuid(), AttemptNumber = 2, ScheduledFor = Now.AddDays(1) },
            ];
            db.FailedPayments.Add(c);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var found = await ServiceOver(read).GetCaseByInvoiceAsync(_merchantA, "in_target");

        Assert.NotNull(found);
        Assert.Equal("Recovered", found.Status);
        Assert.Equal(2, found.AttemptCount);
        Assert.Equal(Now.AddDays(1), found.ClosedAt);
    }

    [Fact]
    public async Task Invoice_lookup_is_null_for_unknown_or_foreign_invoices()
    {
        await using (var db = CreateContext())
        {
            db.FailedPayments.Add(NewCase(_merchantB, "in_belongs_to_b"));
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var svc = ServiceOver(read);

        Assert.Null(await svc.GetCaseByInvoiceAsync(_merchantA, "in_never_failed"));
        Assert.Null(await svc.GetCaseByInvoiceAsync(_merchantA, "in_belongs_to_b"));
    }

    [Fact]
    public async Task Dunning_entries_come_ordered_by_step()
    {
        Guid caseId;
        await using (var db = CreateContext())
        {
            var c = NewCase(_merchantA, "in_dunned");
            caseId = c.Id;
            c.EmailEntries =
            [
                Entry(2, "urgent", Now.AddDays(-2), openedAt: Now.AddDays(-1)),
                Entry(1, "friendly_reminder", Now.AddDays(-4)),
                Entry(3, "final_notice", Now.AddDays(-1), resultedInRecovery: true),
            ];
            db.FailedPayments.Add(c);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var progress = await ServiceOver(read).GetDunningProgressAsync(_merchantA, caseId);

        Assert.NotNull(progress);
        Assert.Equal(caseId, progress.CaseId);
        Assert.Equal([1, 2, 3], progress.Entries.Select(e => e.SequenceStep));
        Assert.Equal(["friendly_reminder", "urgent", "final_notice"], progress.Entries.Select(e => e.EmailType));
        Assert.NotNull(progress.Entries[1].OpenedAt);
        Assert.True(progress.Entries[2].ResultedInRecovery);
    }

    [Fact]
    public async Task Dunning_for_a_retry_only_case_is_an_empty_list_not_null()
    {
        Guid caseId;
        await using (var db = CreateContext())
        {
            var c = NewCase(_merchantA, "in_retries_only");
            caseId = c.Id;
            db.FailedPayments.Add(c);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var progress = await ServiceOver(read).GetDunningProgressAsync(_merchantA, caseId);

        Assert.NotNull(progress);
        Assert.Empty(progress.Entries);
    }

    [Fact]
    public async Task Dunning_is_null_for_unknown_or_foreign_cases()
    {
        Guid foreignCaseId;
        await using (var db = CreateContext())
        {
            var c = NewCase(_merchantB, "in_b_case");
            foreignCaseId = c.Id;
            db.FailedPayments.Add(c);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var svc = ServiceOver(read);

        Assert.Null(await svc.GetDunningProgressAsync(_merchantA, Guid.NewGuid()));
        Assert.Null(await svc.GetDunningProgressAsync(_merchantA, foreignCaseId));
    }

    [Fact]
    public async Task Status_filter_narrows_both_the_page_and_the_total()
    {
        await using (var db = CreateContext())
        {
            db.FailedPayments.AddRange(
                NewCase(_merchantA, "in_1", status: RecoveryStatus.Recovered),
                NewCase(_merchantA, "in_2", status: RecoveryStatus.Recovered),
                NewCase(_merchantA, "in_3", status: RecoveryStatus.Lost),
                NewCase(_merchantA, "in_4"));
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var svc = ServiceOver(read);

        var recovered = await svc.GetRecoveryCasesAsync(_merchantA, 1, 25, RecoveryStatus.Recovered);
        Assert.NotNull(recovered);
        Assert.Equal(2, recovered.TotalCount);
        Assert.All(recovered.Items, i => Assert.Equal("Recovered", i.Status));

        var unfiltered = await svc.GetRecoveryCasesAsync(_merchantA, 1, 25);
        Assert.NotNull(unfiltered);
        Assert.Equal(4, unfiltered.TotalCount);
    }

    private static EmailSequenceEntry Entry(
        int step, string type, DateTime sentAt, DateTime? openedAt = null, bool resultedInRecovery = false) => new()
    {
        Id = Guid.NewGuid(),
        SequenceStep = step,
        EmailType = type,
        SentAt = sentAt,
        OpenedAt = openedAt,
        ResultedInRecovery = resultedInRecovery,
    };
}
