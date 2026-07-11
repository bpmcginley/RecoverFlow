using RecoverFlow.Domain;

namespace RecoverFlow.Application.Dashboard;

public sealed record RecoveryCaseSummary(
    Guid Id,
    string StripeInvoiceId,
    string? CustomerEmail,
    long AmountCents,
    string Currency,
    string Status,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    int AttemptCount);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>The fields stats are computed from — a slim projection of FailedPayment.</summary>
public sealed record CaseSnapshot(
    RecoveryStatus Status,
    long AmountCents,
    DateTime OpenedAt,
    DateTime? RecoveredAt,
    DateTime? LostAt);

public sealed record DashboardStats(
    IReadOnlyDictionary<string, int> CasesByStatus,
    long AmountRecoveredCents,
    long AmountAtRiskCents,
    double RecoveryRate);

public sealed record MerchantDashboard(DashboardStats AllTime, DashboardStats Last30Days);
