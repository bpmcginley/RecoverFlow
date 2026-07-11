using System.Net;

namespace RecoverFlow.Application.Recovery;

public sealed record DunningEmailContent(string EmailType, string Subject, string Html);

/// <summary>
/// Inline dunning templates. Step 1 goes out when the recovery case opens;
/// each later retry-schedule step sends the next escalation.
/// </summary>
public static class DunningEmailTemplates
{
    public static DunningEmailContent ForStep(int step, string merchantName, long amountCents, string currency)
    {
        var amount = $"{amountCents / 100m:0.00} {currency.ToUpperInvariant()}";
        return step switch
        {
            1 => new("friendly_reminder",
                $"Your payment to {merchantName} didn't go through",
                Body(merchantName, amount,
                    "We weren't able to process your latest payment — this usually just means a card has expired or needs updating.",
                    "Please update your card details to keep your subscription active. If you've already done so, no further action is needed.")),
            2 => new("urgent",
                $"Reminder: your {amount} payment to {merchantName} is still due",
                Body(merchantName, amount,
                    "A quick follow-up: we still haven't been able to collect your recent payment.",
                    "Please update your card soon so your subscription isn't interrupted.")),
            _ => new("final_notice",
                $"Final notice: your {merchantName} subscription is at risk",
                Body(merchantName, amount,
                    "We've tried several times to collect your payment without success.",
                    "Please update your card now — this is our last reminder before your subscription is cancelled.")),
        };
    }

    private static string Body(string merchantName, string amount, string lead, string closing)
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
}
