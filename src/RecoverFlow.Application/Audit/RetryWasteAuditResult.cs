namespace RecoverFlow.Application.Audit;

/// <summary>A decline code and how many attempts / how much money it accounts for.</summary>
public sealed record WasteCodeRow(string Code, int Count, long AmountCents);

/// <summary>A card that ran up against Visa's reattempt ceiling.</summary>
public sealed record VisaExposureRow(string CardKey, int PeakAttempts, bool AtOrOverCap);

/// <summary>
/// The finished audit: how much of a merchant's failed revenue sits on codes no retry can
/// collect, how much needs a new card rather than another attempt, how much is genuinely
/// reachable, and where the account stands against Visa's reattempt cap. Everything here is
/// arithmetic on the merchant's own charges — no recovery-rate assumption is applied anywhere,
/// deliberately, exactly as in scripts/retry_waste_audit.py.
/// </summary>
public sealed record RetryWasteAuditResult(
    string Company,
    string Currency,
    int TotalCharges,
    long TotalAmountCents,
    int BlockedCount,
    long BlockedAmountCents,
    IReadOnlyList<WasteCodeRow> BlockedByCode,
    int NeedsNewCardCount,
    long NeedsNewCardAmountCents,
    int ReachableCount,
    bool HadCardData,
    IReadOnlyList<VisaExposureRow> VisaExposure,
    int WastePercent,
    long WasteAmountCents)
{
    /// <summary>An account with no failed charges in the window — the report says "healthy".</summary>
    public bool IsEmpty => TotalCharges == 0;
}
