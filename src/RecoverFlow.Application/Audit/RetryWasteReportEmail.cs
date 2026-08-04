using System.Globalization;
using System.Net;
using System.Text;

namespace RecoverFlow.Application.Audit;

public sealed record RetryWasteEmailContent(string Subject, string Html, string PlainText);

/// <summary>
/// Renders the one-page audit report. Both parts are built from the same numbers; a text
/// alternative is expected on legitimate mail and its absence is a spam-filter signal.
///
/// Every figure here is arithmetic on the merchant's own charges. Nothing in this file
/// estimates what could be recovered, and nothing should be added that does — a report that
/// ends in a projection is the thing every competitor already sends.
/// </summary>
public static class RetryWasteReportEmail
{
    public static RetryWasteEmailContent Render(RetryWasteReport r, string? companyName = null)
    {
        var who = string.IsNullOrWhiteSpace(companyName) ? "your account" : companyName;

        if (r.TotalFailedCount == 0) return Empty(who, Coverage(r));

        var subject = r.RecommendsNoPurchase
            ? $"Your retry waste audit: mostly retryable, you're fine"
            : $"Your retry waste audit: {r.WastedPercent:0}% of failures needed a new card";

        return new RetryWasteEmailContent(subject, Html(r, who), PlainText(r, who));
    }

    private static RetryWasteEmailContent Empty(string who, string coverage) => new(
        "Your retry waste audit: no failed payments found",
        $"<p>We scanned {WebUtility.HtmlEncode(coverage)} of {WebUtility.HtmlEncode(who)} and found no failed charges at all.</p>"
        + "<p>That is the best possible result and it means there is nothing here for us to sell you. "
        + "If you expected failures in this window, check that the audit was authorized on the right Stripe account.</p>",
        $"We scanned {coverage} of {who} and found no failed charges at all.\n\n"
        + "That is the best possible result and it means there is nothing here for us to sell you. "
        + "If you expected failures in this window, check that the audit was authorized on the right Stripe account.\n");

    private static string Money(long cents, string currency)
    {
        var major = cents / 100m;
        return currency.Equals("usd", StringComparison.OrdinalIgnoreCase)
            ? major.ToString("C0", CultureInfo.GetCultureInfo("en-US"))
            : $"{major.ToString("N0", CultureInfo.InvariantCulture)} {currency.ToUpperInvariant()}";
    }

    private static double Pct(int part, int whole) => whole == 0 ? 0 : (double)part / whole * 100;

    /// <summary>
    /// The scan is capped and Stripe pages newest-first, so a busy account's report covers less
    /// than the full window. Saying "last 90 days" anyway would be the exact kind of unearned
    /// number this product exists to argue against.
    /// </summary>
    private static string Coverage(RetryWasteReport r) => r.Truncated && r.EarliestCovered is { } from
        ? $"the period since {from:d MMMM yyyy} (the scan is capped, and this account had enough "
        + $"activity to reach that cap before reaching {r.WindowDays} days)"
        : $"the last {r.WindowDays} days";

    private static string Verdict(RetryWasteReport r) => r.RecommendsNoPurchase
        ? "Most of your failures are the ordinary retryable kind. Stripe's own free Smart Retries "
        + "already cover this, and you should not buy a recovery tool on the strength of this report."
        : $"{r.WastedPercent:0}% of your failed revenue ({Money(r.WastedCents, r.Currency)}) sits on cards that "
        + "needed replacing, not retrying. Any tool that answers this with more retry attempts is "
        + "solving the wrong half of the problem.";

    private static string Html(RetryWasteReport r, string who)
    {
        var s = new StringBuilder();
        s.Append("<div style=\"font-family:-apple-system,Segoe UI,Helvetica,Arial,sans-serif;max-width:640px;line-height:1.5\">");
        s.Append($"<h2 style=\"margin:0 0 4px\">Retry waste audit</h2>");
        s.Append($"<p style=\"color:#666;margin:0 0 24px\">{WebUtility.HtmlEncode(who)} &middot; {WebUtility.HtmlEncode(Coverage(r))}</p>");

        s.Append($"<p><strong>{r.TotalFailedCount:N0} failed charges, {Money(r.TotalFailedCents, r.Currency)} at risk.</strong></p>");

        s.Append("<h3>What could never have worked</h3>");
        s.Append($"<p>{r.BlockedCount:N0} charges ({Pct(r.BlockedCount, r.TotalFailedCount):0}%), "
               + $"{Money(r.BlockedCents, r.Currency)}. These are on the nine decline codes Stripe will not send "
               + "to the card network at all until a new payment method is attached. The retry schedule keeps "
               + "running and the attempt counter keeps climbing, but no attempt is actually made.</p>");
        s.Append(CodeTableHtml(r.BlockedByStripe, r.Currency));

        s.Append("<h3>What needed a new card, not another attempt</h3>");
        s.Append($"<p>{r.NeedsNewCardCount:N0} charges ({Pct(r.NeedsNewCardCount, r.TotalFailedCount):0}%), "
               + $"{Money(r.NeedsNewCardCents, r.Currency)}. Stripe <em>does</em> retry these, so they burn real "
               + "attempts, but the card cannot succeed until the customer replaces it.</p>");
        s.Append(CodeTableHtml(r.NeedsNewCard, r.Currency));

        s.Append("<h3>What was actually reachable by retrying</h3>");
        s.Append($"<p>{r.ReachableCount:N0} charges ({Pct(r.ReachableCount, r.TotalFailedCount):0}%), "
               + $"{Money(r.ReachableCents, r.Currency)}. Everything else needed a conversation, not a schedule.</p>");

        s.Append($"<h3>Visa reattempt budget</h3>");
        s.Append(VisaHtml(r));

        s.Append("<h3>The one sentence version</h3>");
        s.Append($"<p>{WebUtility.HtmlEncode(Verdict(r))}</p>");

        s.Append("<hr style=\"border:none;border-top:1px solid #eee;margin:24px 0\">");
        s.Append("<p style=\"color:#666;font-size:13px\">This audit read your Stripe account in read-only mode. "
               + "No write permissions were requested and none were used. We did not keep your access token.<br>"
               + "Visa's Excessive Reattempts Rule caps reattempts at "
               + $"{RetryWasteAuditService.VisaReattemptCap} per card per {RetryWasteAuditService.VisaWindowDays} days; "
               + "Stripe blocks further attempts past it.</p>");
        s.Append("</div>");
        return s.ToString();
    }

    private static string CodeTableHtml(IReadOnlyList<WasteCodeRow> rows, string currency)
    {
        if (rows.Count == 0) return "<p style=\"color:#666\">None.</p>";

        var s = new StringBuilder("<table style=\"border-collapse:collapse;margin:8px 0 16px\">");
        foreach (var row in rows)
        {
            s.Append("<tr>");
            s.Append($"<td style=\"padding:2px 16px 2px 0\"><code>{WebUtility.HtmlEncode(row.DeclineCode)}</code></td>");
            s.Append($"<td style=\"padding:2px 16px 2px 0;text-align:right\">{row.Count:N0}</td>");
            s.Append($"<td style=\"padding:2px 0;text-align:right\">{Money(row.AmountCents, currency)}</td>");
            s.Append("</tr>");
        }
        return s.Append("</table>").ToString();
    }

    private static string VisaHtml(RetryWasteReport r)
    {
        if (!r.VisaExposureAvailable)
            return "<p style=\"color:#666\">Skipped: none of the failed charges carried card details we could "
                 + "count attempts against.</p>";

        if (r.VisaExposure.Count == 0)
            return $"<p>No card reached 10 failed attempts inside a {RetryWasteAuditService.VisaWindowDays}-day "
                 + "window. Healthy.</p>";

        var over = r.VisaExposure.Count(v => v.AtOrOverCap);
        var s = new StringBuilder($"<p>{r.VisaExposure.Count:N0} card(s) reached 10 or more attempts in "
            + $"{RetryWasteAuditService.VisaWindowDays} days.");
        if (over > 0)
            s.Append($" <strong>{over:N0} of them hit {RetryWasteAuditService.VisaReattemptCap} or more</strong>, "
                   + "where Stripe starts blocking further attempts outright.");
        s.Append("</p><table style=\"border-collapse:collapse;margin:8px 0 16px\">");
        foreach (var v in r.VisaExposure.Take(8))
        {
            s.Append("<tr>");
            s.Append($"<td style=\"padding:2px 16px 2px 0\"><code>{WebUtility.HtmlEncode(v.CardRef)}</code></td>");
            s.Append($"<td style=\"padding:2px 16px 2px 0;text-align:right\">{v.PeakAttemptsInWindow:N0} attempts</td>");
            s.Append($"<td style=\"padding:2px 0;color:#b00\">{(v.AtOrOverCap ? "at or over the cap" : "")}</td>");
            s.Append("</tr>");
        }
        return s.Append("</table>").ToString();
    }

    private static string PlainText(RetryWasteReport r, string who)
    {
        var s = new StringBuilder();
        s.AppendLine($"RETRY WASTE AUDIT: {who}");
        s.AppendLine(new string('=', 62));
        s.AppendLine();
        s.AppendLine($"Covering {Coverage(r)}.");
        s.AppendLine($"{r.TotalFailedCount:N0} failed charges, {Money(r.TotalFailedCents, r.Currency)} at risk.");
        s.AppendLine();

        s.AppendLine("WHAT COULD NEVER HAVE WORKED");
        s.AppendLine(new string('-', 62));
        s.AppendLine($"  {r.BlockedCount:N0} charges ({Pct(r.BlockedCount, r.TotalFailedCount):0}%), "
                   + $"{Money(r.BlockedCents, r.Currency)}, on the nine decline codes Stripe will not");
        s.AppendLine("  send to the card network until a new payment method is attached.");
        AppendCodes(s, r.BlockedByStripe, r.Currency);

        s.AppendLine("WHAT NEEDED A NEW CARD, NOT ANOTHER ATTEMPT");
        s.AppendLine(new string('-', 62));
        s.AppendLine($"  {r.NeedsNewCardCount:N0} charges ({Pct(r.NeedsNewCardCount, r.TotalFailedCount):0}%), "
                   + $"{Money(r.NeedsNewCardCents, r.Currency)}. Stripe does retry these, so");
        s.AppendLine("  they burn attempts, but the card cannot succeed until it is replaced.");
        AppendCodes(s, r.NeedsNewCard, r.Currency);

        s.AppendLine("WHAT WAS ACTUALLY REACHABLE BY RETRYING");
        s.AppendLine(new string('-', 62));
        s.AppendLine($"  {r.ReachableCount:N0} charges ({Pct(r.ReachableCount, r.TotalFailedCount):0}%), "
                   + $"{Money(r.ReachableCents, r.Currency)}.");
        s.AppendLine();

        s.AppendLine($"VISA REATTEMPT BUDGET (cap is {RetryWasteAuditService.VisaReattemptCap} per card "
                   + $"per {RetryWasteAuditService.VisaWindowDays} days)");
        s.AppendLine(new string('-', 62));
        if (!r.VisaExposureAvailable)
            s.AppendLine("  Skipped: no card details on the failed charges.");
        else if (r.VisaExposure.Count == 0)
            s.AppendLine("  No card reached 10 failed attempts in a 30 day window. Healthy.");
        else
        {
            s.AppendLine($"  {r.VisaExposure.Count:N0} card(s) reached 10 or more attempts in 30 days.");
            foreach (var v in r.VisaExposure.Take(8))
                s.AppendLine($"     {v.CardRef,-38} {v.PeakAttemptsInWindow,3} attempts"
                           + (v.AtOrOverCap ? "  <-- at or over the cap" : ""));
        }
        s.AppendLine();

        s.AppendLine("THE ONE SENTENCE VERSION");
        s.AppendLine(new string('-', 62));
        s.AppendLine("  " + Verdict(r));
        s.AppendLine();
        s.AppendLine("This audit read your Stripe account in read-only mode. No write permissions");
        s.AppendLine("were requested and none were used. We did not keep your access token.");
        return s.ToString();
    }

    private static void AppendCodes(StringBuilder s, IReadOnlyList<WasteCodeRow> rows, string currency)
    {
        s.AppendLine();
        if (rows.Count == 0) { s.AppendLine("     (none)"); s.AppendLine(); return; }
        foreach (var row in rows)
            s.AppendLine($"     {row.DeclineCode,-34} {row.Count,5}   {Money(row.AmountCents, currency)}");
        s.AppendLine();
    }
}
