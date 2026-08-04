using RecoverFlow.Application.Audit;
using Stripe;

namespace RecoverFlow.Infrastructure.Stripe;

/// <summary>
/// Reads a connected account's failed charges using the platform key plus the Stripe-Account
/// header, exactly like the other readers. The audit therefore never needs to hold the
/// merchant's OAuth access token at all — the code is exchanged only to learn which account
/// authorized us, and the token is dropped on the floor.
///
/// Charges rather than invoices: the report needs the decline code, the card fingerprint and
/// the attempt timestamp together, and only a charge carries all three. Stripe's charge list
/// has no status filter, so successes are dropped here.
/// </summary>
public sealed class StripeRetryWasteReader : IRetryWasteReader
{
    public async Task<ChargeScanResult> ListFailedChargesAsync(
        string stripeAccountId, DateTime sinceUtc, int maxCharges, CancellationToken ct = default)
    {
        var service = new ChargeService();
        var request = new RequestOptions { StripeAccount = stripeAccountId };
        var options = new ChargeListOptions
        {
            Created = new DateRangeOptions { GreaterThanOrEqual = sinceUtc },
            Limit = 100,
        };
        // payment_method_details (and the card fingerprint on it) ships on the charge by
        // default. It is NOT an expandable field — asking Stripe to expand it is an error.

        var results = new List<FailedChargeAttempt>();
        var examined = 0;
        var truncated = false;
        var oldestSeen = DateTime.MaxValue;

        // Stripe returns newest-first. Hitting the cap therefore means we have a complete
        // picture of the RECENT end of the window and nothing older, so the report must say
        // which dates it actually covers rather than claiming the full window.
        await foreach (var charge in service.ListAutoPagingAsync(options, request, ct))
        {
            if (examined >= maxCharges) { truncated = true; break; }
            examined++;

            if (charge.Created < oldestSeen) oldestSeen = charge.Created;
            if (charge.Status != "failed") continue;

            results.Add(new FailedChargeAttempt(
                charge.Id,
                charge.Amount,
                charge.Currency,
                charge.FailureCode,
                charge.PaymentMethodDetails?.Card?.Fingerprint,
                charge.Created));
        }

        return new ChargeScanResult(
            results,
            truncated,
            EarliestCovered: examined == 0 ? sinceUtc : oldestSeen);
    }
}
