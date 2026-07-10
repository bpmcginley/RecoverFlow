using Microsoft.EntityFrameworkCore;
using RecoverFlow.Domain.Entities;

namespace RecoverFlow.Application.Common;

public interface IAppDbContext
{
    DbSet<Merchant> Merchants { get; }
    DbSet<FailedPayment> FailedPayments { get; }
    DbSet<RetryAttempt> RetryAttempts { get; }
    DbSet<EmailSequenceEntry> EmailSequences { get; }
    DbSet<CardUpdateSession> CardUpdateSessions { get; }
    DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
