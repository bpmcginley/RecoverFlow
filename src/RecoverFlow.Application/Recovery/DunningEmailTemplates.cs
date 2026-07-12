using System.Net;

namespace RecoverFlow.Application.Recovery;

public sealed record DunningEmailContent(string EmailType, string Subject, string Html, string PlainText);

/// <summary>
/// Inline dunning templates. Step 1 goes out when the recovery case opens;
/// each later retry-schedule step sends the next escalation. Every email ships
/// both HTML and a plain-text alternative — a text part is expected on legitimate
/// mail and its absence is a spam-filter signal.
/// </summary>
public static class DunningEmailTemplates
{
    public static DunningEmailContent ForStep(int step, string merchantName, long amountCents, string currency)
    {
        var amount = $"{amountCents / 100m:0.00} {currency.ToUpperInvariant()}";
        var (type, subject, lead, closing) = step switch
        {
            1 => ("friendly_reminder",
                $"Your payment to {merchantName} didn't go through",
                "We weren't able to process your latest payment — this usually just means a card has expired or needs updating.",
                "Please update your card details to keep your subscription active. If you've already done so, no further action is needed."),
            2 => ("urgent",
                $"Reminder: your {amount} payment to {merchantName} is still due",
                "A quick follow-up: we still haven't been able to collect your recent payment.",
                "Please update your card soon so your subscription isn't interrupted."),
            _ => ("final_notice",
                $"Final notice: your {merchantName} subscription is at risk",
                "We've tried several times to collect your payment without success.",
                "Please update your card now — this is our last reminder before your subscription is cancelled."),
        };

        return new DunningEmailContent(type, subject,
            Html(merchantName, amount, lead, closing),
            PlainText(merchantName, amount, lead, closing));
    }

    private static string Html(string merchantName, string amount, string lead, string closing)
    {
        var merchant = WebUtility.HtmlEncode(merchantName);
        return $"""
            <div style="font-family:Arial,Helvetica,sans-serif;max-width:600px;margin:0 auto;color:#333">
              <p>Hi,</p>
              <p>{lead}</p>
              <p>Your payment of <strong>{amount}</strong> to <strong>{merchant}</strong> is currently outstanding.</p>
              <p>{closing}</p>
              <p>— The {merchant} team</p>
            </div>
            """;
    }

    private static string PlainText(string merchantName, string amount, string lead, string closing) =>
        $"""
        Hi,

        {lead}

        Your payment of {amount} to {merchantName} is currently outstanding.

        {closing}

        — The {merchantName} team
        """;
}
