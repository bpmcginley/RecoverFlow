using RecoverFlow.Application.Audit;

namespace RecoverFlow.Tests.Unit;

public class RetryWasteAuditServiceTests
{
    private static readonly DateTime Base = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly RetryWasteAuditService _svc = new();

    private static FailedCharge Charge(
        string? code, long cents = 10_000, string currency = "usd",
        string? card = null, DateTime? when = null) =>
        new(code, cents, currency, when ?? Base, card);

    [Fact]
    public void Empty_history_reports_healthy()
    {
        var r = _svc.Compute([], "Acme");

        Assert.True(r.IsEmpty);
        Assert.Equal(0, r.TotalCharges);
        Assert.Equal(0, r.WastePercent);
        Assert.Contains("healthy", _svc.BuildReport(r).PlainTextBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Blocked_codes_are_counted_and_summed()
    {
        var r = _svc.Compute(
        [
            Charge("stolen_card", 6_000),
            Charge("lost_card", 4_000),
            Charge("insufficient_funds", 10_000), // reachable, not blocked
        ], "Acme");

        Assert.Equal(2, r.BlockedCount);
        Assert.Equal(10_000, r.BlockedAmountCents);
        Assert.Equal(3, r.TotalCharges);
    }

    [Fact]
    public void Blocked_breakdown_groups_by_code_most_frequent_first()
    {
        var r = _svc.Compute(
        [
            Charge("stolen_card", 1_000),
            Charge("stolen_card", 2_000),
            Charge("lost_card", 5_000),
        ], "Acme");

        Assert.Equal("stolen_card", r.BlockedByCode[0].Code);
        Assert.Equal(2, r.BlockedByCode[0].Count);
        Assert.Equal(3_000, r.BlockedByCode[0].AmountCents);
        Assert.Equal("lost_card", r.BlockedByCode[1].Code);
    }

    [Fact]
    public void Needs_new_card_bucket_is_the_four_reissue_codes()
    {
        var r = _svc.Compute(
        [
            Charge("expired_card", 1_000),
            Charge("incorrect_cvc", 2_000),
            Charge("invalid_expiry_month", 3_000),
            Charge("invalid_expiry_year", 4_000),
            Charge("insufficient_funds", 9_000),
        ], "Acme");

        Assert.Equal(4, r.NeedsNewCardCount);
        Assert.Equal(10_000, r.NeedsNewCardAmountCents);
        Assert.Equal(0, r.BlockedCount); // none of these are Stripe-blocked
    }

    [Fact]
    public void Reachable_is_total_minus_blocked_minus_new_card()
    {
        var r = _svc.Compute(
        [
            Charge("stolen_card"),          // blocked
            Charge("expired_card"),         // needs new card
            Charge("insufficient_funds"),   // reachable
            Charge("processing_error"),     // reachable
        ], "Acme");

        Assert.Equal(2, r.ReachableCount);
    }

    [Fact]
    public void One_sentence_waste_is_blocked_plus_new_card()
    {
        // 1 blocked (6000) + 1 new-card (4000) of 4 charges => 50% waste, 10000.
        var r = _svc.Compute(
        [
            Charge("stolen_card", 6_000),
            Charge("expired_card", 4_000),
            Charge("insufficient_funds", 5_000),
            Charge("generic_decline", 5_000),
        ], "Acme");

        Assert.Equal(50, r.WastePercent);
        Assert.Equal(10_000, r.WasteAmountCents);
    }

    [Fact]
    public void Dominant_currency_labels_the_report()
    {
        var r = _svc.Compute(
        [
            Charge("insufficient_funds", 100, "usd"),
            Charge("insufficient_funds", 100, "eur"),
            Charge("insufficient_funds", 100, "eur"),
        ], "Acme");

        Assert.Equal("eur", r.Currency);
    }

    [Fact]
    public void Visa_exposure_surfaces_cards_at_ten_or_more_in_thirty_days()
    {
        var charges = new List<FailedCharge>();
        for (var i = 0; i < 12; i++)
            charges.Add(Charge("insufficient_funds", card: "card_hot", when: Base.AddDays(i)));
        for (var i = 0; i < 5; i++)
            charges.Add(Charge("insufficient_funds", card: "card_cool", when: Base.AddDays(i)));

        var r = _svc.Compute(charges, "Acme");

        var hot = Assert.Single(r.VisaExposure);
        Assert.Equal("card_hot", hot.CardKey);
        Assert.Equal(12, hot.PeakAttempts);
        Assert.False(hot.AtOrOverCap); // 12 < 15
    }

    [Fact]
    public void Visa_exposure_flags_cards_at_or_over_the_cap()
    {
        var charges = new List<FailedCharge>();
        for (var i = 0; i < 16; i++)
            charges.Add(Charge("insufficient_funds", card: "card_blocked", when: Base.AddDays(i)));

        var r = _svc.Compute(charges, "Acme");

        Assert.True(r.VisaExposure.Single().AtOrOverCap);
    }

    [Fact]
    public void Visa_window_does_not_accumulate_attempts_spread_beyond_thirty_days()
    {
        // Two clusters of 6, 40 days apart: peak in any 30-day window is 6, below the threshold.
        var charges = new List<FailedCharge>();
        for (var i = 0; i < 6; i++)
            charges.Add(Charge("insufficient_funds", card: "card_spread", when: Base.AddDays(i)));
        for (var i = 0; i < 6; i++)
            charges.Add(Charge("insufficient_funds", card: "card_spread", when: Base.AddDays(40 + i)));

        var r = _svc.Compute(charges, "Acme");

        Assert.Empty(r.VisaExposure);
    }

    [Fact]
    public void Missing_card_data_skips_the_visa_section()
    {
        var r = _svc.Compute([Charge("insufficient_funds", card: null)], "Acme");

        Assert.False(r.HadCardData);
        Assert.Empty(r.VisaExposure);
        Assert.Contains("Skipped", _svc.BuildReport(r).PlainTextBody);
    }

    [Fact]
    public void Report_titles_with_the_company_and_states_the_waste_line()
    {
        var r = _svc.Compute([Charge("stolen_card", 10_000), Charge("insufficient_funds", 10_000)], "Acme Corp");
        var report = _svc.BuildReport(r);

        Assert.Contains("Acme Corp", report.Subject);
        Assert.Contains("50%", report.PlainTextBody);
        Assert.Contains("Acme Corp", report.HtmlBody);
    }

    [Fact]
    public void Report_html_escapes_the_company_name()
    {
        var r = _svc.Compute([Charge("stolen_card")], "<script>&");
        var html = _svc.BuildReport(r).HtmlBody;

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
