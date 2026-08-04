using RecoverFlow.Application.Audit;

namespace RecoverFlow.Tests.Unit;

public class RetryWasteReportEmailTests
{
    private static FailedChargeAttempt Charge(string? code, long cents, string? card = null, DateTime? at = null) =>
        new($"ch_{Guid.NewGuid():N}", cents, "usd", code, card, at ?? new DateTime(2026, 5, 1));

    private static RetryWasteReport Report(
        IReadOnlyList<FailedChargeAttempt> charges, bool truncated = false) =>
        RetryWasteAuditService.Build(
            new ChargeScanResult(charges, truncated, new DateTime(2026, 6, 12)), 90);

    private static List<FailedChargeAttempt> Typical()
    {
        var start = new DateTime(2026, 5, 1);
        var charges = new List<FailedChargeAttempt>();
        for (var i = 0; i < 14; i++) charges.Add(Charge("insufficient_funds", 4900, "fp_soft", start.AddDays(i * 5)));
        for (var i = 0; i < 9; i++) charges.Add(Charge("lost_card", 4900, $"fp_lost{i}", start.AddDays(i)));
        for (var i = 0; i < 5; i++) charges.Add(Charge("stolen_card", 9900, $"fp_stolen{i}", start.AddDays(i)));
        for (var i = 0; i < 7; i++) charges.Add(Charge("expired_card", 2900, $"fp_exp{i}", start.AddDays(i)));
        // One card hammered well past the Visa ceiling.
        for (var i = 0; i < 17; i++) charges.Add(Charge("do_not_honor", 1900, "fp_hot", start.AddDays(i)));
        return charges;
    }

    [Fact]
    public void Renders_a_typical_account_without_placeholder_artifacts()
    {
        var c = RetryWasteReportEmail.Render(Report(Typical()), "Acme");

        foreach (var body in new[] { c.Html, c.PlainText })
        {
            Assert.DoesNotContain("NaN", body);
            Assert.DoesNotContain("Infinity", body);
            Assert.DoesNotContain("{", body);        // no unsubstituted interpolation
            Assert.DoesNotContain("  ,", body);
        }
        Assert.Contains("Acme", c.PlainText);
        Assert.Contains("17 attempts", c.PlainText);
        Assert.Contains("at or over the cap", c.PlainText);
    }

    [Fact]
    public void Subject_leads_with_the_waste_number_when_there_is_one()
    {
        var c = RetryWasteReportEmail.Render(Report(Typical()));

        Assert.Contains("%", c.Subject);
        Assert.DoesNotContain("you're fine", c.Subject);
    }

    [Fact]
    public void A_clean_account_is_told_to_buy_nothing_in_the_subject_and_the_body()
    {
        var charges = Enumerable.Range(0, 30).Select(_ => Charge("insufficient_funds", 4900)).ToList();

        var c = RetryWasteReportEmail.Render(Report(charges), "Quiet Co");

        Assert.Contains("you're fine", c.Subject);
        Assert.Contains("should not buy", c.PlainText);
        Assert.Contains("Smart Retries", c.PlainText);
    }

    [Fact]
    public void A_truncated_scan_never_claims_the_full_window()
    {
        var c = RetryWasteReportEmail.Render(Report(Typical(), truncated: true), "Busy Co");

        Assert.Contains("12 June 2026", c.PlainText);
        Assert.DoesNotContain("the last 90 days", c.PlainText);
    }

    [Fact]
    public void An_untruncated_scan_states_the_window_plainly()
    {
        var c = RetryWasteReportEmail.Render(Report(Typical()), "Acme");

        Assert.Contains("the last 90 days", c.PlainText);
    }

    [Fact]
    public void No_recovery_estimate_language_leaks_into_the_report()
    {
        var c = RetryWasteReportEmail.Render(Report(Typical()), "Acme");

        // The entire pitch is that we do not put a made-up percentage on their money.
        foreach (var banned in new[] { "recoverable", "we could recover", "uplift", "estimated recovery" })
        {
            Assert.DoesNotContain(banned, c.PlainText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(banned, c.Html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Html_escapes_a_company_name_that_contains_markup()
    {
        var c = RetryWasteReportEmail.Render(Report(Typical()), "<script>alert(1)</script>");

        Assert.DoesNotContain("<script>alert", c.Html);
        Assert.Contains("&lt;script&gt;", c.Html);
    }

    [Fact]
    public void Dump()
    {
        var dir = Environment.GetEnvironmentVariable("RF_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        var c = RetryWasteReportEmail.Render(Report(Typical()), "Acme Software");
        File.WriteAllText(Path.Combine(dir, "report.txt"), c.Subject + "\n\n" + c.PlainText);
        File.WriteAllText(Path.Combine(dir, "report.html"), c.Html);
    }

    [Fact]
    public void The_verdict_never_labels_the_attempt_share_as_a_share_of_money()
    {
        // 3 blocked charges at $10 and 17 reachable at $1: 15% of attempts but 64% of the money.
        var charges = new List<FailedChargeAttempt>();
        for (var i = 0; i < 3; i++) charges.Add(Charge("lost_card", 1000));
        for (var i = 0; i < 17; i++) charges.Add(Charge("insufficient_funds", 100));

        var r = Report(charges);
        var c = RetryWasteReportEmail.Render(r, "Skewed Co");

        Assert.Equal(15, Math.Round(r.WastedPercent));
        Assert.Equal(64, Math.Round(r.WastedRevenuePercent));
        // Asserted against the HTML because the plain-text part is column-wrapped, which can
        // split a phrase across lines. Both numbers must appear, and the one attached to the
        // money must be the revenue share.
        Assert.Contains("15%", c.Html);
        Assert.Contains("64% of the money", c.Html);
    }

    [Fact]
    public void Every_plain_text_line_stays_inside_the_report_column()
    {
        var c = RetryWasteReportEmail.Render(Report(Typical()), "Acme");

        var lines = c.PlainText.Split('\n');
        Assert.All(lines, l => Assert.True(l.TrimEnd().Length <= 78, $"too wide: {l}"));
    }
}
