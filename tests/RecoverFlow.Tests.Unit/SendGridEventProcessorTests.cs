using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RecoverFlow.Application.Recovery;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;

namespace RecoverFlow.Tests.Unit;

public class SendGridEventProcessorTests
{
    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static EmailSequenceEntry SeedEntry(AppDbContext db)
    {
        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            Email = "owner@acme.test",
            CompanyName = "Acme Corp",
            StripeAccountId = $"acct_{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
        };
        var payment = new FailedPayment
        {
            Id = Guid.NewGuid(),
            MerchantId = merchant.Id,
            Merchant = merchant,
            StripeInvoiceId = $"in_{Guid.NewGuid():N}",
            CustomerEmail = "customer@example.com",
            AmountCents = 4200,
            Currency = "usd",
            Status = RecoveryStatus.ActiveRecovery,
            FirstFailedAt = DateTime.UtcNow,
        };
        var entry = new EmailSequenceEntry
        {
            Id = Guid.NewGuid(),
            FailedPaymentId = payment.Id,
            FailedPayment = payment,
            SequenceStep = 1,
            EmailType = "friendly_reminder",
            SentAt = DateTime.UtcNow,
        };
        db.FailedPayments.Add(payment);
        db.EmailSequences.Add(entry);
        db.SaveChanges();
        return entry;
    }

    private static SendGridEventProcessor CreateProcessor(AppDbContext db) =>
        new(db, NullLogger<SendGridEventProcessor>.Instance);

    private static string Event(string kind, Guid entryId, long timestamp = 1_755_000_000, string extra = "") =>
        $$"""{"event":"{{kind}}","timestamp":{{timestamp}},"rf_entry_id":"{{entryId}}"{{extra}}}""";

    [Fact]
    public async Task Open_event_stamps_OpenedAt_from_event_timestamp()
    {
        using var db = CreateDb();
        var entry = SeedEntry(db);

        await CreateProcessor(db).ProcessAsync($"[{Event("open", entry.Id)}]");

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_755_000_000).UtcDateTime, entry.OpenedAt);
        Assert.Null(entry.ClickedAt);
    }

    [Fact]
    public async Task Click_event_stamps_ClickedAt_only()
    {
        using var db = CreateDb();
        var entry = SeedEntry(db);

        await CreateProcessor(db).ProcessAsync($"[{Event("click", entry.Id)}]");

        Assert.Null(entry.OpenedAt);
        Assert.NotNull(entry.ClickedAt);
    }

    [Fact]
    public async Task First_open_wins_over_later_opens()
    {
        using var db = CreateDb();
        var entry = SeedEntry(db);
        var processor = CreateProcessor(db);

        await processor.ProcessAsync($"[{Event("open", entry.Id, 1_755_000_000)}]");
        await processor.ProcessAsync($"[{Event("open", entry.Id, 1_755_100_000)}]");

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_755_000_000).UtcDateTime, entry.OpenedAt);
    }

    [Fact]
    public async Task Machine_opens_are_ignored()
    {
        using var db = CreateDb();
        var entry = SeedEntry(db);

        await CreateProcessor(db).ProcessAsync(
            $"[{Event("open", entry.Id, extra: ",\"sg_machine_open\":true")}]");

        Assert.Null(entry.OpenedAt);
    }

    [Fact]
    public async Task Unknown_entries_and_other_event_types_are_skipped()
    {
        using var db = CreateDb();
        var entry = SeedEntry(db);

        var batch = "[" + string.Join(",",
            Event("delivered", entry.Id),                                        // not an open/click
            Event("open", Guid.NewGuid()),                                       // no such entry
            """{"event":"open","timestamp":1755000000}""",                       // no tracking arg
            """{"event":"open","timestamp":1755000000,"rf_entry_id":"nope"}""")  // malformed id
            + "]";

        await CreateProcessor(db).ProcessAsync(batch);

        Assert.Null(entry.OpenedAt);
        Assert.Null(entry.ClickedAt);
    }

    [Fact]
    public async Task Batch_with_open_and_click_stamps_both()
    {
        using var db = CreateDb();
        var entry = SeedEntry(db);

        await CreateProcessor(db).ProcessAsync(
            $"[{Event("open", entry.Id)},{Event("click", entry.Id, 1_755_000_060)}]");

        Assert.NotNull(entry.OpenedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_755_000_060).UtcDateTime, entry.ClickedAt);
    }
}
