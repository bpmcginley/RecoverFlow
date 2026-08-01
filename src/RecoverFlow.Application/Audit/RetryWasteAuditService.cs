using System.Globalization;
using System.Text;
using RecoverFlow.Domain.Services;

namespace RecoverFlow.Application.Audit;

/// <summary>
/// The "Retry Waste Audit" made a service: it counts the failed charges that no retry could
/// ever have collected. This is the C# port of scripts/retry_waste_audit.py, kept deliberately
/// in lockstep so the marketplace app and the CSV tool produce the same numbers on the same data.
///
/// Pure and Stripe-free: <see cref="Compute"/> takes charges and returns the result, and
/// <see cref="BuildReport"/> renders the one-pager. Both are unit-tested without any Stripe call.
/// </summary>
public sealed class RetryWasteAuditService
{
    // Retried by Stripe, but the card itself is unusable until the customer supplies a new one.
    // Narrower than the classifier's RequiresCardUpdate: the blocked codes (lost/stolen/pickup/
    // incorrect_number) are excluded here because Stripe won't even put those on the wire.
    private static readonly HashSet<string> NeedsNewCard =
    [
        "expired_card", "incorrect_cvc", "invalid_expiry_month", "invalid_expiry_year"
    ];

    // Visa Excessive Reattempts Rule, enforced since April 2022: no more than 15 reattempts
    // per card per 30 days, past which Stripe blocks further attempts outright.
    private const int VisaCap = 15;
    private const int VisaWindowDays = 30;
    private const int VisaReportThreshold = 10; // Surface a card once it reaches 10 in a window.

    public RetryWasteAuditResult Compute(IReadOnlyList<FailedCharge> charges, string company)
    {
        if (charges.Count == 0)
            return new RetryWasteAuditResult(
                company, "usd", 0, 0, 0, 0, [], 0, 0, 0, false, [], 0, 0);

        // Dominant currency labels the report; amounts follow the CSV tool and are not
        // currency-scoped, so the marketplace app and the script agree on the same file.
        var currency = charges
            .GroupBy(c => (c.Currency ?? "usd").ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .First().Key;

        var total = charges.Count;
        var totalAmount = charges.Sum(c => c.AmountCents);

        // Normalize each decline code once, then bucket. IsBlockedByStripe already treats null as
        // "not blocked", so blocked codes are always non-null by the time they're grouped.
        var coded = charges.Select(c => (Charge: c, Code: Normalize(c.DeclineCode))).ToList();
        var blocked = coded.Where(x => DeclineCodeClassifier.IsBlockedByStripe(x.Code)).ToList();
        var needsNewCard = coded.Where(x => x.Code is not null && NeedsNewCard.Contains(x.Code)).ToList();

        var blockedByCode = blocked
            .GroupBy(x => x.Code!)
            .Select(g => new WasteCodeRow(g.Key, g.Count(), g.Sum(x => x.Charge.AmountCents)))
            .OrderByDescending(r => r.Count)
            .ToList();

        // reachable = (not blocked) minus (needs a new card). Everything else needed a
        // conversation, not a schedule.
        var reachable = total - blocked.Count - needsNewCard.Count;

        var hadCardData = charges.Any(c => !string.IsNullOrEmpty(c.CardKey));
        var exposure = VisaExposure(charges);

        var wasteCount = blocked.Count + needsNewCard.Count;
        var wasteAmount = blocked.Sum(x => x.Charge.AmountCents) + needsNewCard.Sum(x => x.Charge.AmountCents);
        var wastePercent = (int)Math.Round(wasteCount * 100.0 / total, MidpointRounding.AwayFromZero);

        return new RetryWasteAuditResult(
            company,
            currency,
            total,
            totalAmount,
            blocked.Count,
            blocked.Sum(x => x.Charge.AmountCents),
            blockedByCode,
            needsNewCard.Count,
            needsNewCard.Sum(x => x.Charge.AmountCents),
            reachable,
            hadCardData,
            exposure,
            wastePercent,
            wasteAmount);
    }

    /// <summary>
    /// Peak reattempts on any single card within a rolling 30-day window. Cards that reach 10+
    /// are surfaced; those at/over 15 are flagged as the point Stripe starts blocking. Mirrors
    /// visa_exposure() in the Python: for each card, the max count of later attempts that fall
    /// within 30 days of some earlier attempt.
    /// </summary>
    private static List<VisaExposureRow> VisaExposure(IReadOnlyList<FailedCharge> charges)
    {
        var perCard = charges
            .Where(c => !string.IsNullOrEmpty(c.CardKey))
            .GroupBy(c => c.CardKey!)
            .Select(g => (Key: g.Key, Dates: g.Select(c => c.CreatedUtc).OrderBy(d => d).ToList()));

        var worst = new List<VisaExposureRow>();
        foreach (var (key, dates) in perCard)
        {
            var peak = 0;
            for (var i = 0; i < dates.Count; i++)
            {
                var count = dates.Skip(i).TakeWhile(d => d - dates[i] <= TimeSpan.FromDays(VisaWindowDays)).Count();
                peak = Math.Max(peak, count);
            }
            if (peak >= VisaReportThreshold)
                worst.Add(new VisaExposureRow(key, peak, peak >= VisaCap));
        }

        return worst.OrderByDescending(w => w.PeakAttempts).ToList();
    }

    private static string? Normalize(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToLowerInvariant();

    // ---- Report rendering ---------------------------------------------------------------

    public sealed record Report(string Subject, string HtmlBody, string PlainTextBody);

    public Report BuildReport(RetryWasteAuditResult r)
    {
        var subject = $"Your Retry Waste Audit — {r.Company}";
        return new Report(subject, RenderHtml(r), RenderText(r));
    }

    private static string Money(long cents, string currency)
    {
        var major = cents / 100.0;
        return currency.Equals("usd", StringComparison.OrdinalIgnoreCase)
            ? "$" + major.ToString("N0", CultureInfo.InvariantCulture)
            : major.ToString("N0", CultureInfo.InvariantCulture) + " " + currency.ToUpperInvariant();
    }

    private static int Pct(int part, int total) =>
        total == 0 ? 0 : (int)Math.Round(part * 100.0 / total, MidpointRounding.AwayFromZero);

    private static string RenderText(RetryWasteAuditResult r)
    {
        var cur = r.Currency;
        var sb = new StringBuilder();
        sb.AppendLine($"RETRY WASTE AUDIT: {r.Company}");
        sb.AppendLine(new string('=', 62));

        if (r.IsEmpty)
        {
            sb.AppendLine();
            sb.AppendLine("No failed charges in the last 90 days, so nothing was wasted on retries");
            sb.AppendLine("that could never work. That is healthy: Stripe's own free Smart Retries");
            sb.AppendLine("are covering you, and you do not need to buy anything.");
            return sb.ToString();
        }

        sb.AppendLine();
        sb.AppendLine($"{r.TotalCharges:N0} failed charges, {Money(r.TotalAmountCents, cur)} at risk");
        sb.AppendLine();

        sb.AppendLine("WHAT COULD NEVER HAVE WORKED");
        sb.AppendLine(new string('-', 62));
        sb.AppendLine($"  {r.BlockedCount:N0} charges ({Pct(r.BlockedCount, r.TotalCharges)}%) are on the nine codes Stripe will not");
        sb.AppendLine($"  retry at all without a new card. That is {Money(r.BlockedAmountCents, cur)} that no retry");
        sb.AppendLine("  schedule, however clever, was ever going to collect.");
        sb.AppendLine();
        foreach (var row in r.BlockedByCode)
            sb.AppendLine($"     {row.Code,-34} {row.Count,5}   {Money(row.AmountCents, cur)}");
        sb.AppendLine();

        sb.AppendLine("WHAT NEEDED A NEW CARD, NOT ANOTHER ATTEMPT");
        sb.AppendLine(new string('-', 62));
        sb.AppendLine($"  {r.NeedsNewCardCount:N0} charges ({Pct(r.NeedsNewCardCount, r.TotalCharges)}%), {Money(r.NeedsNewCardAmountCents, cur)}.");
        sb.AppendLine("  Stripe DOES retry these, so they burn attempts, but the card cannot");
        sb.AppendLine("  succeed until the customer replaces it.");
        sb.AppendLine();

        sb.AppendLine("WHAT WAS ACTUALLY REACHABLE BY RETRYING");
        sb.AppendLine(new string('-', 62));
        sb.AppendLine($"  {r.ReachableCount:N0} charges ({Pct(r.ReachableCount, r.TotalCharges)}%). Everything else needed a");
        sb.AppendLine("  conversation, not a schedule.");
        sb.AppendLine();

        sb.AppendLine($"VISA REATTEMPT BUDGET (cap is {VisaCap} per card per {VisaWindowDays} days)");
        sb.AppendLine(new string('-', 62));
        if (!r.HadCardData)
        {
            sb.AppendLine("  Skipped: no card fingerprint was available on these charges.");
        }
        else if (r.VisaExposure.Count == 0)
        {
            sb.AppendLine("  No card reached 10 failed attempts in a 30 day window. Healthy.");
        }
        else
        {
            var over = r.VisaExposure.Count(e => e.AtOrOverCap);
            sb.AppendLine($"  {r.VisaExposure.Count} card(s) reached 10 or more attempts in 30 days.");
            if (over > 0)
                sb.AppendLine($"  {over} of them hit {VisaCap} or more, where Stripe starts blocking further attempts.");
            foreach (var e in r.VisaExposure.Take(8))
            {
                var flag = e.AtOrOverCap ? "  <-- at or over the cap" : "";
                var key = e.CardKey.Length > 38 ? e.CardKey[..38] : e.CardKey;
                sb.AppendLine($"     {key,-38} {e.PeakAttempts,3} attempts{flag}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("THE ONE SENTENCE VERSION");
        sb.AppendLine(new string('-', 62));
        sb.AppendLine($"  {r.WastePercent}% of {r.Company}'s failed revenue ({Money(r.WasteAmountCents, cur)}) sits on");
        sb.AppendLine("  cards that needed replacing, not retrying. Any tool that answers this");
        sb.AppendLine("  with more retry attempts is solving the wrong half of the problem.");
        sb.AppendLine();
        sb.AppendLine("Read-only audit. The access token is discarded now the report is sent.");
        sb.AppendLine("Questions: admin@recoverflow.org");
        return sb.ToString();
    }

    private static string RenderHtml(RetryWasteAuditResult r)
    {
        var cur = r.Currency;
        var sb = new StringBuilder();
        sb.Append("""
            <div style="font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;max-width:640px;margin:0 auto;color:#111;line-height:1.5">
            """);
        sb.Append($"<h1 style=\"font-size:20px;margin:0 0 4px\">Retry Waste Audit</h1>");
        sb.Append($"<p style=\"color:#555;margin:0 0 20px\">{Escape(r.Company)}</p>");

        if (r.IsEmpty)
        {
            sb.Append("<p>No failed charges in the last 90 days, so nothing was wasted on retries that could never work. That is healthy: Stripe's own free Smart Retries are covering you, and you do not need to buy anything.</p>");
            sb.Append(Footer());
            sb.Append("</div>");
            return sb.ToString();
        }

        sb.Append($"<p style=\"font-size:16px\"><strong>{r.TotalCharges:N0} failed charges</strong>, {Escape(Money(r.TotalAmountCents, cur))} at risk.</p>");

        sb.Append(Section("What could never have worked",
            $"<strong>{r.BlockedCount:N0} charges ({Pct(r.BlockedCount, r.TotalCharges)}%)</strong> are on the nine codes Stripe will not retry at all without a new card — <strong>{Escape(Money(r.BlockedAmountCents, cur))}</strong> that no retry schedule was ever going to collect."));

        if (r.BlockedByCode.Count > 0)
        {
            sb.Append("<table style=\"border-collapse:collapse;margin:8px 0 0;font-size:14px\">");
            foreach (var row in r.BlockedByCode)
                sb.Append($"<tr><td style=\"padding:2px 16px 2px 0;font-family:monospace\">{Escape(row.Code)}</td><td style=\"padding:2px 16px 2px 0;text-align:right\">{row.Count}</td><td style=\"padding:2px 0;text-align:right\">{Escape(Money(row.AmountCents, cur))}</td></tr>");
            sb.Append("</table>");
        }

        sb.Append(Section("What needed a new card, not another attempt",
            $"<strong>{r.NeedsNewCardCount:N0} charges ({Pct(r.NeedsNewCardCount, r.TotalCharges)}%)</strong>, {Escape(Money(r.NeedsNewCardAmountCents, cur))}. Stripe does retry these, so they burn attempts, but the card cannot succeed until the customer replaces it."));

        sb.Append(Section("What was actually reachable by retrying",
            $"<strong>{r.ReachableCount:N0} charges ({Pct(r.ReachableCount, r.TotalCharges)}%)</strong>. Everything else needed a conversation, not a schedule."));

        var visaBody = !r.HadCardData
            ? "Skipped: no card fingerprint was available on these charges."
            : r.VisaExposure.Count == 0
                ? "No card reached 10 failed attempts in a 30-day window. Healthy."
                : VisaHtml(r);
        sb.Append(Section($"Visa reattempt budget (cap is {VisaCap} per card per {VisaWindowDays} days)", visaBody));

        sb.Append($"""
            <div style="margin:24px 0 0;padding:16px;background:#f6f8fa;border-radius:8px">
            <strong>The one-sentence version.</strong> {r.WastePercent}% of {Escape(r.Company)}'s failed revenue ({Escape(Money(r.WasteAmountCents, cur))}) sits on cards that needed replacing, not retrying. Any tool that answers this with more retry attempts is solving the wrong half of the problem.
            </div>
            """);
        sb.Append(Footer());
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string VisaHtml(RetryWasteAuditResult r)
    {
        var over = r.VisaExposure.Count(e => e.AtOrOverCap);
        var sb = new StringBuilder();
        sb.Append($"{r.VisaExposure.Count} card(s) reached 10 or more attempts in 30 days.");
        if (over > 0)
            sb.Append($" {over} hit {VisaCap}+, where Stripe starts blocking further attempts.");
        sb.Append("<table style=\"border-collapse:collapse;margin:8px 0 0;font-size:14px\">");
        foreach (var e in r.VisaExposure.Take(8))
        {
            var key = e.CardKey.Length > 38 ? e.CardKey[..38] : e.CardKey;
            var flag = e.AtOrOverCap ? " <span style=\"color:#b00\">at/over cap</span>" : "";
            sb.Append($"<tr><td style=\"padding:2px 16px 2px 0;font-family:monospace\">{Escape(key)}</td><td style=\"padding:2px 0\">{e.PeakAttempts} attempts{flag}</td></tr>");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    private static string Section(string title, string body) =>
        $"<h2 style=\"font-size:15px;margin:22px 0 4px;border-top:1px solid #eee;padding-top:16px\">{Escape(title)}</h2><p style=\"margin:0\">{body}</p>";

    private static string Footer() =>
        "<p style=\"color:#888;font-size:12px;margin:28px 0 0;border-top:1px solid #eee;padding-top:12px\">Read-only audit. The access token is discarded now the report is sent. Questions: admin@recoverflow.org</p>";

    private static string Escape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
