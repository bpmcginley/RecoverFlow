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
    int AttemptCount,
    /// <summary>How much of this recovery went back out as a refund or chargeback. Shown
    /// beside the amount rather than deducted from it, so the row still reconciles against
    /// the Stripe invoice the merchant is looking at.</summary>
    long ReversedCents = 0,
    /// <summary>"refund" or "dispute"; null if the recovery still stands.</summary>
    string? ReversalReason = null);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>The fields stats are computed from — a slim projection of FailedPayment.</summary>
public sealed record CaseSnapshot(
    RecoveryStatus Status,
    long AmountCents,
    DateTime OpenedAt,
    DateTime? RecoveredAt,
    DateTime? LostAt,
    long ReversedCents = 0);

public sealed record DashboardStats(
    IReadOnlyDictionary<string, int> CasesByStatus,
    /// <summary>Recovered revenue the merchant still has: gross recoveries less anything
    /// since refunded or charged back. This is the figure billing charges on, so the two
    /// agree; the gross is available separately rather than being the headline.</summary>
    long AmountRecoveredCents,
    long AmountAtRiskCents,
    double RecoveryRate,
    /// <summary>Recovered before refunds and chargebacks are taken off.</summary>
    long AmountRecoveredGrossCents = 0,
    /// <summary>Recovered and then handed back. Zero for almost every merchant, and worth
    /// showing rather than hiding when it is not.</summary>
    long AmountReversedCents = 0);

public sealed record MerchantDashboard(DashboardStats AllTime, DashboardStats Last30Days);

/// <summary>An uncapped total with a capped newest-first page; the embedded app's per-customer view.</summary>
public sealed record CaseList(IReadOnlyList<RecoveryCaseSummary> Items, int TotalCount);

public sealed record DunningEntry(
    int SequenceStep,
    string EmailType,
    DateTime SentAt,
    DateTime? OpenedAt,
    DateTime? ClickedAt,
    bool ResultedInRecovery);

/// <summary>Dunning-email progress for one recovery case, ordered by sequence step.
/// An empty entry list is a normal state: smart-retry-only cases send no emails.</summary>
public sealed record DunningProgress(Guid CaseId, IReadOnlyList<DunningEntry> Entries);
