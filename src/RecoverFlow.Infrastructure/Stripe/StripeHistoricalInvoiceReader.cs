using RecoverFlow.Application.Backtest;
using Stripe;

namespace RecoverFlow.Infrastructure.Stripe;

/// <summary>
/// Reads a connected merchant's uncollected invoices (platform key + Stripe-Account header).
/// Two passes — "open" (still owed, past due) and "uncollectible" (Stripe gave up) — are the
/// honest "lost / at-risk" buckets; paid and draft invoices are irrelevant to a recovery backtest.
///
/// Historical decline codes are intentionally not fetched: on the current Stripe API the invoice→
/// charge link is a 4-level expand chain (data.payments.payment.payment_intent) that's brittle and
/// rate-limit heavy over 90 days. The lost-dollar figure (AmountRemaining) is exact regardless;
/// declines are left null so the backtest scores them in the conservative "unknown" bucket.
/// Live recovery cases still classify declines precisely — that data arrives on the webhook.
/// </summary>
public sealed class StripeHistoricalInvoiceReader : IHistoricalInvoiceReader
{
    private static readonly string[] LostStatuses = ["open", "uncollectible"];

    public async Task<IReadOnlyList<HistoricalFailedInvoice>> ListUncollectedInvoicesAsync(
        string stripeAccountId, DateTime sinceUtc, int maxInvoices, CancellationToken ct = default)
    {
        var service = new InvoiceService();
        var request = new RequestOptions { StripeAccount = stripeAccountId };
        var results = new List<HistoricalFailedInvoice>();

        foreach (var status in LostStatuses)
        {
            var options = new InvoiceListOptions
            {
                Status = status,
                CollectionMethod = "charge_automatically",
                Created = new DateRangeOptions { GreaterThanOrEqual = sinceUtc },
                Limit = 100,
            };

            await foreach (var inv in service.ListAutoPagingAsync(options, request, ct))
            {
                // Only invoices that were actually attempted and still owe money are "lost".
                if (!inv.Attempted || inv.AmountRemaining <= 0) continue;

                results.Add(new HistoricalFailedInvoice(
                    inv.Id, inv.AmountRemaining, inv.Currency, DeclineCode: null));

                if (results.Count >= maxInvoices) return results;
            }
        }

        return results;
    }
}
