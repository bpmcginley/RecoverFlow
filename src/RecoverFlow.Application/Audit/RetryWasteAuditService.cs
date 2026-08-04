using RecoverFlow.Domain.Services;

namespace RecoverFlow.Application.Audit;

/// <summary>
/// Splits a merchant's failed charges into the three buckets that matter, and counts their
/// exposure against Visa's reattempt ceiling. Mirrors scripts/retry_waste_audit.py, which is
/// the version run by hand during validation — the two must agree or the pitch and the product
/// tell merchants different things.
///
/// No recovery rates are applied anywhere. Deliberate: every competitor answers a failed payment
/// with an estimate of how much more they could retry, and the entire argument here is that some
/// of those attempts were never going to reach the card network at all.
/// </summary>
public static class RetryWasteAuditService
{
    /// <summary>
    /// Visa's Excessive Reattempts Rule, enforced since April 2022: no more than 15 reattempts
    /// per card per 30 days, past which Stripe blocks further attempts outright.
    /// https://support.stripe.com/questions/payment-blocked-due-to-excessive-retries
    /// </summary>
    public const int VisaReattemptCap = 15;

    public const int VisaWindowDays = 30;

    /// <summary>Cards below this never reach the report; only near-cap cards are actionable.</summary>
    private const int ReportingThreshold = 10;

    public static RetryWasteReport Build(ChargeScanResult scan, int windowDays)
    {
        var charges = scan.Charges;

        if (charges.Count == 0)
            return new RetryWasteReport(
                windowDays, "usd", 0, 0, [], [], 0, 0, [], VisaExposureAvailable: false,
                scan.Truncated, scan.EarliestCovered);

        // Mixing currencies into one total would be meaningless, so the report speaks about the
        // account's dominant currency and says nothing about the rest.
        var currency = charges
            .GroupBy(c => c.Currency.ToLowerInvariant())
            .OrderByDescending(g => g.Sum(c => c.AmountCents))
            .First().Key;

        var scoped = charges
            .Where(c => c.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Both predicates come from DeclineCodeClassifier so this file and the live retry engine
        // can never drift apart on what "blocked" means. Needs-new-card is the remainder:
        // Stripe does retry these, so they burn attempts, but the card cannot succeed until the
        // customer replaces it.
        var blocked = scoped.Where(c => DeclineCodeClassifier.IsBlockedByStripe(c.DeclineCode)).ToList();
        var needsNewCard = scoped
            .Where(c => DeclineCodeClassifier.RequiresCardUpdate(c.DeclineCode)
                     && !DeclineCodeClassifier.IsBlockedByStripe(c.DeclineCode))
            .ToList();

        var reachable = scoped.Count - blocked.Count - needsNewCard.Count;
        var reachableCents = scoped.Sum(c => c.AmountCents)
                           - blocked.Sum(c => c.AmountCents)
                           - needsNewCard.Sum(c => c.AmountCents);

        var exposure = VisaExposure(scoped);

        return new RetryWasteReport(
            windowDays,
            currency,
            scoped.Count,
            scoped.Sum(c => c.AmountCents),
            GroupByCode(blocked),
            GroupByCode(needsNewCard),
            reachable,
            reachableCents,
            exposure,
            VisaExposureAvailable: scoped.Any(c => c.CardFingerprint is not null),
            scan.Truncated,
            scan.EarliestCovered);
    }

    private static List<WasteCodeRow> GroupByCode(List<FailedChargeAttempt> charges) => charges
        .GroupBy(c => c.DeclineCode ?? "unknown")
        .Select(g => new WasteCodeRow(g.Key, g.Count(), g.Sum(c => c.AmountCents)))
        .OrderByDescending(r => r.AmountCents)
        .ToList();

    /// <summary>
    /// Peak attempts against a single card inside any rolling 30-day window. Counted per card
    /// rather than per customer because the cap is a card-level rule — one customer replacing a
    /// card resets it, and charging two cards the same day does not.
    /// </summary>
    private static List<VisaExposureRow> VisaExposure(List<FailedChargeAttempt> charges)
    {
        var window = TimeSpan.FromDays(VisaWindowDays);

        return charges
            .Where(c => c.CardFingerprint is not null)
            .GroupBy(c => c.CardFingerprint!)
            .Select(g => new { Card = g.Key, Peak = PeakInWindow([.. g.Select(c => c.CreatedUtc).Order()], window) })
            .Where(x => x.Peak >= ReportingThreshold)
            .OrderByDescending(x => x.Peak)
            .Select(x => new VisaExposureRow(x.Card, x.Peak, x.Peak >= VisaReattemptCap))
            .ToList();
    }

    // Two pointers over sorted timestamps: for each window start, advance the end as far as the
    // window allows. O(n) per card rather than the O(n^2) rescan the Python prototype does.
    private static int PeakInWindow(IReadOnlyList<DateTime> sorted, TimeSpan window)
    {
        var peak = 0;
        var start = 0;
        for (var end = 0; end < sorted.Count; end++)
        {
            while (sorted[end] - sorted[start] > window) start++;
            peak = Math.Max(peak, end - start + 1);
        }
        return peak;
    }
}
