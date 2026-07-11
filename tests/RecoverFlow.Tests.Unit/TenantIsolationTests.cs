using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;

namespace RecoverFlow.Tests.Unit;

public sealed class TenantIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly Guid _merchantA;
    private readonly Guid _merchantB;

    public TenantIsolationTests()
    {
        // Shared in-memory Sqlite so multiple contexts (per tenant) see the same rows.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();

        (_merchantA, _merchantB) = (Guid.NewGuid(), Guid.NewGuid());
        db.Merchants.AddRange(NewMerchant(_merchantA, "a"), NewMerchant(_merchantB, "b"));
        db.FailedPayments.AddRange(NewPayment(_merchantA, "in_a"), NewPayment(_merchantB, "in_b"));
        db.SaveChanges();
    }

    private AppDbContext CreateContext(Guid? merchantId = null) =>
        new(_options, new TenantContext { MerchantId = merchantId });

    [Fact]
    public async Task Tenant_scope_sees_only_its_own_rows()
    {
        await using var db = CreateContext(_merchantA);

        Assert.Equal(_merchantA, Assert.Single(await db.Merchants.ToListAsync()).Id);
        var payment = Assert.Single(await db.FailedPayments.ToListAsync());
        Assert.Equal("in_a", payment.StripeInvoiceId);
        Assert.Equal(_merchantA, Assert.Single(await db.RetryAttempts.ToListAsync()).FailedPayment.MerchantId);
        Assert.Empty(await db.FailedPayments.Where(p => p.MerchantId == _merchantB).ToListAsync());
    }

    [Fact]
    public async Task Untenanted_scope_sees_everything()
    {
        await using var db = CreateContext();

        Assert.Equal(2, await db.Merchants.CountAsync());
        Assert.Equal(2, await db.FailedPayments.CountAsync());
        Assert.Equal(2, await db.RetryAttempts.CountAsync());
    }

    [Fact]
    public async Task Missing_tenant_context_behaves_as_untenanted()
    {
        // Design-time factory and any direct construction pass no tenant at all.
        await using var db = new AppDbContext(_options);

        Assert.Equal(2, await db.FailedPayments.CountAsync());
    }

    [Fact]
    public async Task Untenanted_scope_can_write_across_merchants()
    {
        await using (var db = CreateContext())
        {
            db.FailedPayments.Add(NewPayment(_merchantB, "in_b2"));
            await db.SaveChangesAsync();
        }

        await using var check = CreateContext(_merchantB);
        Assert.Equal(2, await check.FailedPayments.CountAsync());
    }

    public void Dispose() => _connection.Dispose();

    private static Merchant NewMerchant(Guid id, string tag) => new()
    {
        Id = id,
        Email = $"{tag}@example.com",
        CompanyName = tag,
        StripeAccountId = $"acct_{tag}",
        CreatedAt = DateTime.UtcNow,
    };

    private static FailedPayment NewPayment(Guid merchantId, string invoiceId) => new()
    {
        Id = Guid.NewGuid(),
        MerchantId = merchantId,
        StripeInvoiceId = invoiceId,
        Currency = "usd",
        AmountCents = 1999,
        FirstFailedAt = DateTime.UtcNow,
        RetryAttempts = [new RetryAttempt { Id = Guid.NewGuid(), AttemptNumber = 1, ScheduledFor = DateTime.UtcNow }],
    };
}
