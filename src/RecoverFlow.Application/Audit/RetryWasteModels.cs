namespace RecoverFlow.Application.Audit;

/// <summary>
/// One failed charge attempt. Charge-level rather than invoice-level because the Visa
/// reattempt budget is counted per card per rolling window, and only charges carry both
/// the card fingerprint and the attempt timestamp.
/// </summary>
public sealed record FailedChargeAttempt(
    string ChargeId,
    long AmountCents,
    string Currency,
    string? DeclineCode,
    string? CardFingerprint,
    DateTime CreatedUtc);

/// <summary>
/// What the scan found, plus whether it saw the whole window. The scan is capped so a busy
/// account cannot stall the request, and Stripe pages newest-first, so hitting the cap means
/// the older end of the window was never read. The report has to say so: claiming to cover
/// 90 days while actually covering nine is exactly the kind of number this product exists
/// to argue against.
/// </summary>
public sealed record ChargeScanResult(
    IReadOnlyList<FailedChargeAttempt> Charges,
    bool Truncated,
    DateTime EarliestCovered);

/// <summary>
/// Reads a merchant's recent failed charges. Deliberately separate from
/// <see cref="Backtest.IHistoricalInvoiceReader"/>: that one skips decline codes on purpose,
/// and decline codes are the entire point of this audit.
/// </summary>
public interface IRetryWasteReader
{
    Task<ChargeScanResult> ListFailedChargesAsync(
        string stripeAccountId, DateTime sinceUtc, int maxCharges, CancellationToken ct = default);
}

/// <summary>Failed charges grouped under one decline code.</summary>
public sealed record WasteCodeRow(string DeclineCode, int Count, long AmountCents);

/// <summary>A card that came close to, or passed, Visa's reattempt ceiling.</summary>
public sealed record VisaExposureRow(string CardRef, int PeakAttemptsInWindow, bool AtOrOverCap);

/// <summary>
/// The whole report. Every field is arithmetic over the merchant's own charges — there is
/// deliberately no recovery-rate estimate anywhere in this type, unlike
/// <see cref="Backtest.BacktestRecoveryModel"/>. The pitch is that the number is theirs.
/// </summary>
public sealed record RetryWasteReport(
    int WindowDays,
    string Currency,
    int TotalFailedCount,
    long TotalFailedCents,
    IReadOnlyList<WasteCodeRow> BlockedByStripe,
    IReadOnlyList<WasteCodeRow> NeedsNewCard,
    int ReachableCount,
    long ReachableCents,
    IReadOnlyList<VisaExposureRow> VisaExposure,
    bool VisaExposureAvailable,
    bool Truncated = false,
    DateTime? EarliestCovered = null)
{
    public int BlockedCount => BlockedByStripe.Sum(r => r.Count);
    public long BlockedCents => BlockedByStripe.Sum(r => r.AmountCents);
    public int NeedsNewCardCount => NeedsNewCard.Sum(r => r.Count);
    public long NeedsNewCardCents => NeedsNewCard.Sum(r => r.AmountCents);

    /// <summary>Attempts that could not have succeeded without the customer supplying a new card.</summary>
    public long WastedCents => BlockedCents + NeedsNewCardCents;

    public double WastedPercent =>
        TotalFailedCount == 0 ? 0 : (double)(BlockedCount + NeedsNewCardCount) / TotalFailedCount * 100;

    /// <summary>
    /// Below this, the honest answer is that Stripe's own free Smart Retries already cover them
    /// and they should buy nothing. Sending that verdict is the point, not a fallback.
    /// </summary>
    public bool RecommendsNoPurchase => WastedPercent < 15;
}
