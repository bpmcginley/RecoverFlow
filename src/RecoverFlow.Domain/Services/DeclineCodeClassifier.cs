namespace RecoverFlow.Domain.Services;

public static class DeclineCodeClassifier
{
    private static readonly HashSet<string> HardDeclineCodes =
    [
        "stolen_card", "fraudulent", "pickup_card",
        "lost_card", "restricted_card", "security_violation",
        "card_velocity_exceeded", "merchant_blacklist"
    ];

    private static readonly HashSet<string> SoftDeclineCodes =
    [
        "insufficient_funds", "processing_error",
        "generic_decline", "expired_card", "incorrect_cvc",
        "incorrect_number", "card_declined", "try_again_later",
        "do_not_honor", "transaction_not_allowed"
    ];

    public static DeclineType Classify(string? declineCode)
    {
        if (string.IsNullOrEmpty(declineCode)) return DeclineType.Unknown;
        if (HardDeclineCodes.Contains(declineCode)) return DeclineType.HardDecline;
        if (SoftDeclineCodes.Contains(declineCode)) return DeclineType.SoftDecline;
        return DeclineType.Unknown; // Treat unknown as soft (attempt recovery)
    }

    public static bool ShouldRetry(string? declineCode) =>
        Classify(declineCode) != DeclineType.HardDecline;

    public static bool RequiresCardUpdate(string? declineCode) =>
        declineCode is "expired_card" or "incorrect_cvc"
            or "incorrect_number" or "invalid_expiry_year"
            or "invalid_expiry_month";
}
