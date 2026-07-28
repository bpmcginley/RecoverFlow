using RecoverFlow.Domain;
using RecoverFlow.Domain.Services;

namespace RecoverFlow.Tests.Unit;

public class DeclineCodeClassifierTests
{
    // Stripe's own list of codes it will not execute a retry for without a new
    // payment method. Scheduling against any of these is wasted latency.
    [Theory]
    [InlineData("incorrect_number")]
    [InlineData("lost_card")]
    [InlineData("pickup_card")]
    [InlineData("stolen_card")]
    [InlineData("revocation_of_authorization")]
    [InlineData("revocation_of_all_authorizations")]
    [InlineData("authentication_required")]
    [InlineData("highest_risk_level")]
    [InlineData("transaction_not_allowed")]
    public void Codes_stripe_will_not_execute_are_never_retried(string code)
    {
        Assert.True(DeclineCodeClassifier.IsBlockedByStripe(code));
        Assert.Equal(DeclineType.HardDecline, DeclineCodeClassifier.Classify(code));
        Assert.False(DeclineCodeClassifier.ShouldRetry(code));
    }

    [Theory]
    [InlineData("fraudulent")]
    [InlineData("merchant_blacklist")]
    [InlineData("restricted_card")]
    [InlineData("security_violation")]
    public void Codes_we_decline_to_retry_by_choice_are_hard_but_not_stripe_blocked(string code)
    {
        Assert.False(DeclineCodeClassifier.IsBlockedByStripe(code));
        Assert.Equal(DeclineType.HardDecline, DeclineCodeClassifier.Classify(code));
        Assert.False(DeclineCodeClassifier.ShouldRetry(code));
    }

    [Theory]
    [InlineData("insufficient_funds")]
    [InlineData("processing_error")]
    [InlineData("generic_decline")]
    [InlineData("expired_card")]
    [InlineData("incorrect_cvc")]
    [InlineData("do_not_honor")]
    [InlineData("try_again_later")]
    [InlineData("card_velocity_exceeded")]
    public void Soft_declines_are_classified_and_retried(string code)
    {
        Assert.Equal(DeclineType.SoftDecline, DeclineCodeClassifier.Classify(code));
        Assert.True(DeclineCodeClassifier.ShouldRetry(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some_new_stripe_code")]
    public void Unknown_codes_are_treated_as_recoverable(string? code)
    {
        Assert.Equal(DeclineType.Unknown, DeclineCodeClassifier.Classify(code));
        Assert.True(DeclineCodeClassifier.ShouldRetry(code));
        Assert.False(DeclineCodeClassifier.IsBlockedByStripe(code));
    }

    // Regression: these two were classified soft, so we scheduled retries Stripe
    // was never going to execute, and the connect-time backtest quoted a recovery
    // range for them as if they were ordinary transient failures.
    [Theory]
    [InlineData("incorrect_number")]
    [InlineData("transaction_not_allowed")]
    public void Blocked_codes_are_not_mistaken_for_soft_declines(string code) =>
        Assert.NotEqual(DeclineType.SoftDecline, DeclineCodeClassifier.Classify(code));

    [Theory]
    [InlineData("expired_card", true)]
    [InlineData("incorrect_cvc", true)]
    [InlineData("incorrect_number", true)]
    [InlineData("invalid_expiry_year", true)]
    [InlineData("lost_card", true)]
    [InlineData("stolen_card", true)]
    [InlineData("pickup_card", true)]
    [InlineData("insufficient_funds", false)]
    [InlineData("do_not_honor", false)]
    public void Card_update_is_required_for_bad_data_and_reissued_cards(string code, bool expected) =>
        Assert.Equal(expected, DeclineCodeClassifier.RequiresCardUpdate(code));
}
