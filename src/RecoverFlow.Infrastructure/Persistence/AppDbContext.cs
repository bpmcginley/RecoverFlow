using Microsoft.EntityFrameworkCore;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain.Entities;

namespace RecoverFlow.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenant = null)
    : DbContext(options), IAppDbContext
{
    // Query filters below reference this instance property, so EF re-evaluates it per
    // query against the current scope's tenant. Null (no tenant resolved, or design
    // time) disables the filters entirely.
    private Guid? CurrentMerchantId => tenant?.MerchantId;

    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<FailedPayment> FailedPayments => Set<FailedPayment>();
    public DbSet<RetryAttempt> RetryAttempts => Set<RetryAttempt>();
    public DbSet<EmailSequenceEntry> EmailSequences => Set<EmailSequenceEntry>();
    public DbSet<CardUpdateSession> CardUpdateSessions => Set<CardUpdateSession>();
    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();
    public DbSet<FeeInvoice> FeeInvoices => Set<FeeInvoice>();
    public DbSet<AccountBacktest> AccountBacktests => Set<AccountBacktest>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Merchant>(e =>
        {
            e.Property(m => m.Email).HasMaxLength(320);
            e.Property(m => m.CompanyName).HasMaxLength(200);
            e.Property(m => m.StripeAccountId).HasMaxLength(255);
            e.Property(m => m.StripePlatformCustomerId).HasMaxLength(255);
            e.Property(m => m.Plan).HasMaxLength(50);
            e.Property(m => m.SettingsJson).HasColumnType("jsonb");
            e.HasIndex(m => m.StripeAccountId).IsUnique();
            e.HasQueryFilter(m => CurrentMerchantId == null || m.Id == CurrentMerchantId);
        });

        b.Entity<FailedPayment>(e =>
        {
            e.Property(p => p.StripeInvoiceId).HasMaxLength(255);
            e.Property(p => p.StripeSubscriptionId).HasMaxLength(255);
            e.Property(p => p.StripeCustomerId).HasMaxLength(255);
            e.Property(p => p.CustomerEmail).HasMaxLength(320);
            e.Property(p => p.Currency).HasMaxLength(3);
            e.Property(p => p.DeclineCode).HasMaxLength(100);
            e.Property(p => p.FailureType).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.RecoveryMethod).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(p => new { p.MerchantId, p.StripeInvoiceId }).IsUnique();
            e.HasIndex(p => p.Status);
            e.HasIndex(p => p.FeeInvoiceId);
            e.HasOne(p => p.Merchant).WithMany(m => m.FailedPayments).HasForeignKey(p => p.MerchantId);
            e.HasOne<FeeInvoice>().WithMany().HasForeignKey(p => p.FeeInvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(p => CurrentMerchantId == null || p.MerchantId == CurrentMerchantId);
        });

        b.Entity<FeeInvoice>(e =>
        {
            e.Property(f => f.PeriodLabel).HasMaxLength(7);
            e.Property(f => f.Currency).HasMaxLength(3);
            e.Property(f => f.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(f => f.StripeInvoiceId).HasMaxLength(255);
            e.Property(f => f.HostedInvoiceUrl).HasMaxLength(2048);
            e.Property(f => f.FailureReason).HasMaxLength(1000);
            e.HasIndex(f => f.MerchantId);
            e.HasIndex(f => f.Status);
            e.HasOne(f => f.Merchant).WithMany().HasForeignKey(f => f.MerchantId);
            e.HasQueryFilter(f => CurrentMerchantId == null || f.MerchantId == CurrentMerchantId);
        });

        b.Entity<AccountBacktest>(e =>
        {
            e.Property(a => a.Currency).HasMaxLength(3);
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.BreakdownJson).HasColumnType("jsonb");
            e.Property(a => a.FailureReason).HasMaxLength(1000);
            e.HasIndex(a => a.MerchantId);
            e.HasOne(a => a.Merchant).WithMany().HasForeignKey(a => a.MerchantId);
            e.HasQueryFilter(a => CurrentMerchantId == null || a.MerchantId == CurrentMerchantId);
        });

        b.Entity<RetryAttempt>(e =>
        {
            e.Property(a => a.Result).HasMaxLength(20);
            e.Property(a => a.DeclineCodeReceived).HasMaxLength(100);
            e.HasIndex(a => a.ScheduledFor);
            e.HasOne(a => a.FailedPayment).WithMany(p => p.RetryAttempts).HasForeignKey(a => a.FailedPaymentId);
            e.HasQueryFilter(a => CurrentMerchantId == null || a.FailedPayment.MerchantId == CurrentMerchantId);
        });

        b.Entity<EmailSequenceEntry>(e =>
        {
            e.Property(s => s.EmailType).HasMaxLength(50);
            e.HasOne(s => s.FailedPayment).WithMany(p => p.EmailEntries).HasForeignKey(s => s.FailedPaymentId);
            e.HasQueryFilter(s => CurrentMerchantId == null || s.FailedPayment.MerchantId == CurrentMerchantId);
        });

        b.Entity<CardUpdateSession>(e =>
        {
            e.Property(s => s.Token).HasMaxLength(64);
            e.Property(s => s.NewPaymentMethodId).HasMaxLength(255);
            e.HasIndex(s => s.Token).IsUnique();
            e.HasOne(s => s.FailedPayment).WithMany().HasForeignKey(s => s.FailedPaymentId);
            e.HasQueryFilter(s => CurrentMerchantId == null || s.FailedPayment.MerchantId == CurrentMerchantId);
        });

        b.Entity<ProcessedWebhookEvent>(e =>
        {
            e.HasKey(w => w.EventId);
            e.Property(w => w.EventId).HasMaxLength(255);
            e.Property(w => w.EventType).HasMaxLength(100);
        });
    }
}
