using RecoverFlow.Domain;
using RecoverFlow.Domain.Services;

namespace RecoverFlow.Tests.Unit;

public class DeclineCodeClassifierTests
{
    [Theory]
    [InlineData("stolen_card")]
    [InlineData("fraudulent")]
    [InlineData("pickup_card")]
    [InlineData("lost_card")]
    [InlineData("restricted_card")]
    public void Hard_declines_are_classified_and_never_retried(string code)
    {
        Assert.Equal(DeclineType.HardDecline, DeclineCodeClassifier.Classify(code));
        Assert.False(DeclineCodeClassifier.ShouldRetry(code));
    }

    [Theory]
    [InlineData("insufficient_funds")]
    [InlineData("processing_error")]
    [InlineData("generic_decline")]
    [InlineData("expired_card")]
    [InlineData("incorrect_cvc")]
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
    }

    [Theory]
    [InlineData("expired_card", true)]
    [InlineData("incorrect_cvc", true)]
    [InlineData("incorrect_number", true)]
    [InlineData("invalid_expiry_year", true)]
    [InlineData("insufficient_funds", false)]
    [InlineData("stolen_card", false)]
    public void Card_update_is_required_only_for_card_data_problems(string code, bool expected) =>
        Assert.Equal(expected, DeclineCodeClassifier.RequiresCardUpdate(code));
}
