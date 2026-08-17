namespace RecoverFlow.Domain.Services;

public static class DeclineCodeClassifier
{
    /// <summary>
    /// Stripe will not execute a retry for these until a new payment method is attached.
    /// It keeps the schedule running and keeps incrementing attempt_count, but the charge
    /// never reaches the card network, so scheduling our own retry buys nothing but delay.
    /// Verified 28 July 2026 against
    /// https://docs.stripe.com/billing/revenue-recovery/smart-retries
    /// Re-checked 16 August 2026 against the same page: unchanged, still these nine.
    /// This set is also published as data at /data/stripe-decline-codes.json by
    /// scripts/build_decline_dataset.py, which reads it by name from this file.
    /// </summary>
    private static readonly HashSet<string> RetryBlockedByStripe =
    [
        "incorrect_number", "lost_card", "pickup_card", "stolen_card",
        "revocation_of_authorization", "revocation_of_all_authorizations",
        "authentication_required", "highest_risk_level", "transaction_not_allowed"
    ];

    /// <summary>
    /// Codes Stripe would retry but we will not: the answer is settled, and asking
    /// again costs goodwill or starts to look like card testing.
    /// </summary>
    private static readonly HashSet<string> NotWorthRetrying =
    [
        "fraudulent", "merchant_blacklist", "restricted_card", "security_violation"
    ];

    /// <summary>
    /// Temporary conditions. The same card can succeed later without the customer
    /// doing anything, which is what makes a retry schedule worth having at all.
    /// </summary>
    private static readonly HashSet<string> SoftDeclineCodes =
    [
        "insufficient_funds", "processing_error", "generic_decline", "expired_card",
        "incorrect_cvc", "card_declined", "try_again_later", "do_not_honor",
        "card_velocity_exceeded", "invalid_expiry_month", "invalid_expiry_year"
    ];

    /// <summary>
    /// True when Stripe itself refuses to put the charge on the wire without a new
    /// payment method. Narrower than <see cref="DeclineType.HardDecline"/>, which also
    /// covers codes we decline to retry by choice.
    /// </summary>
    public static bool IsBlockedByStripe(string? declineCode) =>
        declineCode is not null && RetryBlockedByStripe.Contains(declineCode);

    public static DeclineType Classify(string? declineCode) => declineCode switch
    {
        null or "" => DeclineType.Unknown,
        var c when RetryBlockedByStripe.Contains(c) => DeclineType.HardDecline,
        var c when NotWorthRetrying.Contains(c) => DeclineType.HardDecline,
        var c when SoftDeclineCodes.Contains(c) => DeclineType.SoftDecline,
        _ => DeclineType.Unknown, // New or undocumented code: attempt recovery.
    };

    public static bool ShouldRetry(string? declineCode) =>
        Classify(declineCode) != DeclineType.HardDecline;

    /// <summary>
    /// The customer has to supply new card details. Covers bad card data and the
    /// reissue cases, where the replacement card is usually already in their wallet
    /// and the fix costs them under a minute.
    /// </summary>
    public static bool RequiresCardUpdate(string? declineCode) =>
        declineCode is "expired_card" or "incorrect_cvc" or "incorrect_number"
            or "invalid_expiry_year" or "invalid_expiry_month"
            or "lost_card" or "stolen_card" or "pickup_card";
}
