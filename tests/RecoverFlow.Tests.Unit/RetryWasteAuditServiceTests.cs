using RecoverFlow.Application.Audit;

namespace RecoverFlow.Tests.Unit;

public class RetryWasteAuditServiceTests
{
    private static FailedChargeAttempt Charge(
        string? code, long cents = 1000, string currency = "usd", string? card = null, DateTime? at = null) =>
        new($"ch_{Guid.NewGuid():N}", cents, currency, code, card, at ?? new DateTime(2026, 1, 1));

    [Fact]
    public void Empty_input_reports_nothing_rather_than_dividing_by_zero()
    {
        var r = RetryWasteAuditService.Build([], 90);

        Assert.Equal(0, r.TotalFailedCount);
        Assert.Equal(0, r.WastedPercent);
        Assert.False(r.VisaExposureAvailable);
    }

    [Theory]
    // The nine codes Stripe will not put on the wire without a new payment method.
    [InlineData("incorrect_number")]
    [InlineData("lost_card")]
    [InlineData("pickup_card")]
    [InlineData("stolen_card")]
    [InlineData("revocation_of_authorization")]
    [InlineData("revocation_of_all_authorizations")]
    [InlineData("authentication_required")]
    [InlineData("highest_risk_level")]
    [InlineData("transaction_not_allowed")]
    public void Blocked_codes_land_in_the_blocked_bucket(string code)
    {
        var r = RetryWasteAuditService.Build([Charge(code)], 90);

        Assert.Equal(1, r.BlockedCount);
        Assert.Equal(0, r.NeedsNewCardCount);
        Assert.Equal(0, r.ReachableCount);
    }

    [Theory]
    // Stripe does retry these, so they burn attempts, but the card cannot succeed
    // until the customer replaces it. Must not double-count into the blocked bucket.
    [InlineData("expired_card")]
    [InlineData("incorrect_cvc")]
    [InlineData("invalid_expiry_month")]
    [InlineData("invalid_expiry_year")]
    public void Needs_new_card_codes_are_separate_from_blocked(string code)
    {
        var r = RetryWasteAuditService.Build([Charge(code)], 90);

        Assert.Equal(0, r.BlockedCount);
        Assert.Equal(1, r.NeedsNewCardCount);
        Assert.Equal(0, r.ReachableCount);
    }

    [Theory]
    [InlineData("insufficient_funds")]
    [InlineData("do_not_honor")]
    [InlineData("processing_error")]
    [InlineData("generic_decline")]
    public void Soft_declines_count_as_genuinely_reachable(string code)
    {
        var r = RetryWasteAuditService.Build([Charge(code)], 90);

        Assert.Equal(0, r.BlockedCount);
        Assert.Equal(0, r.NeedsNewCardCount);
        Assert.Equal(1, r.ReachableCount);
    }

    [Fact]
    public void Buckets_partition_the_input_exactly_once()
    {
        List<FailedChargeAttempt> charges =
        [
            Charge("lost_card", 500), Charge("stolen_card", 500),   // blocked
            Charge("expired_card", 300),                            // needs new card
            Charge("insufficient_funds", 200), Charge(null, 100),   // reachable
        ];

        var r = RetryWasteAuditService.Build(charges, 90);

        Assert.Equal(5, r.TotalFailedCount);
        Assert.Equal(2 + 1 + 2, r.BlockedCount + r.NeedsNewCardCount + r.ReachableCount);
        Assert.Equal(1600, r.TotalFailedCents);
        Assert.Equal(1600, r.BlockedCents + r.NeedsNewCardCents + r.ReachableCents);
    }

    [Fact]
    public void Wasted_percent_covers_blocked_plus_needs_new_card()
    {
        List<FailedChargeAttempt> charges =
        [
            Charge("lost_card"), Charge("expired_card"),      // 2 wasted
            Charge("insufficient_funds"), Charge("do_not_honor"), // 2 reachable
        ];

        var r = RetryWasteAuditService.Build(charges, 90);

        Assert.Equal(50, r.WastedPercent);
        Assert.Equal(2000, r.WastedCents);
    }

    [Fact]
    public void A_mostly_retryable_account_is_told_to_buy_nothing()
    {
        var charges = Enumerable.Range(0, 20).Select(_ => Charge("insufficient_funds")).ToList();
        charges.Add(Charge("lost_card")); // ~4.8% wasted

        var r = RetryWasteAuditService.Build(charges, 90);

        Assert.True(r.RecommendsNoPurchase);
    }

    [Fact]
    public void A_high_waste_account_is_not_told_to_buy_nothing()
    {
        List<FailedChargeAttempt> charges =
        [
            Charge("lost_card"), Charge("stolen_card"), Charge("expired_card"),
            Charge("insufficient_funds"),
        ];

        var r = RetryWasteAuditService.Build(charges, 90);

        Assert.False(r.RecommendsNoPurchase);
    }

    [Fact]
    public void Report_speaks_only_about_the_dominant_currency()
    {
        List<FailedChargeAttempt> charges =
        [
            Charge("lost_card", 10_000, "usd"),
            Charge("lost_card", 10_000, "usd"),
            Charge("lost_card", 500, "eur"),
        ];

        var r = RetryWasteAuditService.Build(charges, 90);

        Assert.Equal("usd", r.Currency);
        Assert.Equal(2, r.TotalFailedCount);
        Assert.Equal(20_000, r.TotalFailedCents);
    }

    [Fact]
    public void Visa_exposure_is_skipped_when_no_charge_carries_a_card()
    {
        var r = RetryWasteAuditService.Build([Charge("insufficient_funds"), Charge("lost_card")], 90);

        Assert.False(r.VisaExposureAvailable);
        Assert.Empty(r.VisaExposure);
    }

    [Fact]
    public void Cards_below_the_reporting_threshold_are_not_listed()
    {
        var start = new DateTime(2026, 1, 1);
        var charges = Enumerable.Range(0, 9)
            .Select(i => Charge("insufficient_funds", card: "fp_quiet", at: start.AddDays(i)))
            .ToList();

        var r = RetryWasteAuditService.Build(charges, 90);

        Assert.True(r.VisaExposureAvailable);
        Assert.Empty(r.VisaExposure);
    }

    [Fact]
    public void A_card_over_the_cap_inside_the_window_is_flagged()
    {
        var start = new DateTime(2026, 1, 1);
        var charges = Enumerable.Range(0, 17)
            .Select(i => Charge("insufficient_funds", card: "fp_hot", at: start.AddDays(i)))
            .ToList();

        var r = RetryWasteAuditService.Build(charges, 90);

        var row = Assert.Single(r.VisaExposure);
        Assert.Equal("fp_hot", row.CardRef);
        Assert.True(row.AtOrOverCap);
        // 17 attempts one day apart: the widest 30-day window holds all 17.
        Assert.Equal(17, row.PeakAttemptsInWindow);
    }

    [Fact]
    public void Attempts_spread_beyond_the_window_do_not_accumulate()
    {
        // 20 attempts, 10 days apart, spans 190 days. No 30-day window holds more than 4.
        var start = new DateTime(2026, 1, 1);
        var charges = Enumerable.Range(0, 20)
            .Select(i => Charge("insufficient_funds", card: "fp_spread", at: start.AddDays(i * 10)))
            .ToList();

        var r = RetryWasteAuditService.Build(charges, 90);

        Assert.Empty(r.VisaExposure);
    }

    [Fact]
    public void Exposure_is_counted_per_card_not_per_account()
    {
        var start = new DateTime(2026, 1, 1);
        var charges = new List<FailedChargeAttempt>();
        foreach (var card in new[] { "fp_a", "fp_b" })
            charges.AddRange(Enumerable.Range(0, 9)
                .Select(i => Charge("insufficient_funds", card: card, at: start.AddDays(i))));

        var r = RetryWasteAuditService.Build(charges, 90);

        // 18 attempts in the window overall, but 9 each: neither card is reportable.
        Assert.Equal(18, r.TotalFailedCount);
        Assert.Empty(r.VisaExposure);
    }

    [Fact]
    public void Code_rows_are_grouped_and_ordered_by_amount()
    {
        List<FailedChargeAttempt> charges =
        [
            Charge("lost_card", 100),
            Charge("stolen_card", 5_000),
            Charge("lost_card", 100),
        ];

        var r = RetryWasteAuditService.Build(charges, 90);

        Assert.Collection(r.BlockedByStripe,
            first => { Assert.Equal("stolen_card", first.DeclineCode); Assert.Equal(1, first.Count); },
            second => { Assert.Equal("lost_card", second.DeclineCode); Assert.Equal(200, second.AmountCents); });
    }
}
