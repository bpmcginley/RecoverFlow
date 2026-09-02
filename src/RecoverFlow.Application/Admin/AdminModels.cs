namespace RecoverFlow.Application.Admin;

/// <summary>
/// An amount in one currency. Every money figure on the admin page is a list of these
/// rather than a single number: cents in different currencies are not addable, and the
/// page once summed them and labelled the result in dollars.
/// </summary>
public sealed record Money(string Currency, long Cents);

public sealed record DayCount(string Date, int Count);

/// <summary>Failures opened and recoveries closed on one day, as counts. Counts are
/// currency-neutral, which is what lets the two series share an axis.</summary>
public sealed record DayActivity(string Date, int Failed, int Recovered);

/// <summary>Cases grouped by status and currency; the shape the first version of the page read.</summary>
public sealed record StatusMoney(string Status, string Currency, int Count, long Cents);

public sealed record AdminBacktest(
    Guid Id,
    Guid MerchantId,
    string CompanyName,
    string Status,
    int WindowDays,
    int FailedInvoiceCount,
    long FailedAmountCents,
    long RecoverableLowCents,
    long RecoverableHighCents,
    string Currency,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record AdminMerchant(
    Guid Id,
    string Email,
    string CompanyName,
    string Plan,
    DateTime CreatedAt,
    string StripeAccountId,
    /// <summary>Has a token and has not uninstalled. The token alone only says "installed once".</summary>
    bool Connected,
    DateTime? DisconnectedAtUtc,
    int ActiveCases,
    int RecoveredCases,
    int LostCases,
    /// <summary>Gross recoveries; refunds and chargebacks are shown beside it, not taken off.</summary>
    IReadOnlyList<Money> Recovered,
    /// <summary>Open cases still being worked.</summary>
    IReadOnlyList<Money> AtRisk,
    IReadOnlyList<Money> Reversed,
    /// <summary>Our fee actually put on an invoice for this merchant's recoveries.</summary>
    IReadOnlyList<Money> FeesBilled,
    DateTime? LastFailureAtUtc,
    DateTime? LastRecoveryAtUtc,
    /// <summary>Their newest complete scan, which is their current picture; null if none finished.</summary>
    AdminBacktest? LatestScan);

public sealed record AdminCase(
    Guid Id,
    Guid MerchantId,
    string CompanyName,
    string StripeInvoiceId,
    string? CustomerEmail,
    long AmountCents,
    string Currency,
    string Status,
    string FailureType,
    string? DeclineCode,
    string Method,
    DateTime FirstFailedAt,
    DateTime? RecoveredAt,
    DateTime? LostAt,
    long ReversedAmountCents,
    string? ReversalReason,
    DateTime? BilledAtUtc,
    int Retries,
    int Emails);

public sealed record DeclineBreakdown(string DeclineCode, string FailureType, int Count, int Recovered, int Lost);

public sealed record MethodBreakdown(string Method, int Count);

public sealed record RetryStats(int Total, int Succeeded, int Failed, int Skipped, int Pending);

public sealed record EmailStepStats(int Step, string EmailType, int Sent, int Opened, int Clicked, int RecoveredAfter);

public sealed record AdminFeeInvoice(
    Guid Id,
    Guid MerchantId,
    string CompanyName,
    string PeriodLabel,
    long BillableRecoveredCents,
    int RecoveredCaseCount,
    long FeeCents,
    long ReversalCreditCents,
    long FloorTopUpCents,
    long TotalCents,
    string Currency,
    string Status,
    string? StripeInvoiceId,
    string? HostedInvoiceUrl,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc);

public sealed record WebhookTypeCount(string EventType, int Count);

public sealed record WebhookStats(
    int Total,
    DateTime? LastAtUtc,
    IReadOnlyList<WebhookTypeCount> ByTypeInWindow,
    IReadOnlyList<DayCount> ByDay);

public sealed record AdminTotals(
    int Merchants,
    int Connected,
    int Disconnected,
    int SignupsInWindow,
    int Backtests,
    int BacktestsComplete,
    int AccountsScanned,
    IReadOnlyList<Money> FailedAmountScanned,
    int ActiveCases,
    int RecoveredCases,
    int LostCases,
    int CancelledCases,
    IReadOnlyList<Money> Recovered,
    IReadOnlyList<Money> RecoveredInWindow,
    IReadOnlyList<Money> Reversed,
    IReadOnlyList<Money> AtRisk,
    /// <summary>Fee invoices Stripe accepted and emailed. Paid or not is not tracked.</summary>
    IReadOnlyList<Money> FeesInvoiced,
    int WebhookEvents,
    DateTime? LastWebhookAtUtc);

public sealed record AdminStats(
    DateTime GeneratedAtUtc,
    int WindowDays,
    AdminTotals Totals,
    IReadOnlyList<DayCount> SignupsByDay,
    IReadOnlyList<DayActivity> ActivityByDay,
    IReadOnlyList<StatusMoney> RecoveryCases,
    IReadOnlyList<AdminMerchant> Merchants,
    IReadOnlyList<AdminBacktest> Backtests,
    /// <summary>Newest cases across every merchant, capped; the per-merchant drill-down filters these.</summary>
    IReadOnlyList<AdminCase> Cases,
    IReadOnlyList<DeclineBreakdown> Declines,
    IReadOnlyList<MethodBreakdown> Methods,
    RetryStats Retries,
    IReadOnlyList<EmailStepStats> Emails,
    IReadOnlyList<AdminFeeInvoice> FeeInvoices,
    WebhookStats Webhooks);
