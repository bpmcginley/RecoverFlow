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
/// has no status filter, so successes are dropped here; the scan is capped on charges examined
/// so a busy account cannot stall the request or burn through rate limits.
/// </summary>
public sealed class StripeRetryWasteReader : IRetryWasteReader
{
    public async Task<IReadOnlyList<FailedChargeAttempt>> ListFailedChargesAsync(
        string stripeAccountId, DateTime sinceUtc, int maxCharges, CancellationToken ct = default)
    {
        var service = new ChargeService();
        var request = new RequestOptions { StripeAccount = stripeAccountId };
        var options = new ChargeListOptions
        {
            Created = new DateRangeOptions { GreaterThanOrEqual = sinceUtc },
            Limit = 100,
        };
        // The card fingerprint is what makes per-card Visa counting possible across reissued
        // payment method ids, and it only arrives when payment_method_details is expanded.
        options.AddExpand("data.payment_method_details");

        var results = new List<FailedChargeAttempt>();
        var examined = 0;

        await foreach (var charge in service.ListAutoPagingAsync(options, request, ct))
        {
            if (++examined > maxCharges) break;
            if (charge.Status != "failed") continue;

            results.Add(new FailedChargeAttempt(
                charge.Id,
                charge.Amount,
                charge.Currency,
                charge.FailureCode,
                charge.PaymentMethodDetails?.Card?.Fingerprint,
                charge.Created));
        }

        return results;
    }
}
