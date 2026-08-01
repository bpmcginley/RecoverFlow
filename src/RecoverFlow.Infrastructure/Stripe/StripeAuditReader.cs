using RecoverFlow.Application.Audit;
using Stripe;

namespace RecoverFlow.Infrastructure.Stripe;

/// <summary>
/// Read-only Stripe access for the Retry Waste Audit marketplace app. Uses the platform key
/// plus the Stripe-Account header (the same pattern as <see cref="StripeHistoricalInvoiceReader"/>),
/// so a merchant's OAuth access token is never held or stored — it is used once to confirm the
/// account id and then discarded by the caller.
/// </summary>
public sealed class StripeAuditReader : IStripeAuditReader
{
    // Don't page a huge account forever hunting for failures; the 90-day window and this ceiling
    // both bound the scan. Whichever hits first wins.
    private const int MaxChargesScanned = 5000;

    public async Task<AuditAccount> GetAccountAsync(string stripeAccountId, CancellationToken ct = default)
    {
        // Platform retrieves the connected account by id — no account header, no merchant token.
        var account = await new AccountService().GetAsync(stripeAccountId, cancellationToken: ct);
        var company = account.BusinessProfile?.Name
            ?? account.Settings?.Dashboard?.DisplayName;
        return new AuditAccount(account.Email, company);
    }

    public async Task<IReadOnlyList<FailedCharge>> ListFailedChargesAsync(
        string stripeAccountId, DateTime sinceUtc, int maxCharges, CancellationToken ct = default)
    {
        var request = new RequestOptions { StripeAccount = stripeAccountId };
        var options = new ChargeListOptions
        {
            Created = new DateRangeOptions { GreaterThanOrEqual = sinceUtc },
            Limit = 100,
        };

        var results = new List<FailedCharge>();
        var scanned = 0;

        await foreach (var charge in new ChargeService().ListAutoPagingAsync(options, request, ct))
        {
            if (++scanned > MaxChargesScanned) break;
            if (!string.Equals(charge.Status, "failed", StringComparison.Ordinal)) continue;

            // failure_code carries the decline code on a failed card charge; outcome.reason is
            // the fallback when it's absent. Card fingerprint groups reattempts of the same card
            // across charges for the Visa budget; customer id is the fallback when it isn't set.
            var declineCode = !string.IsNullOrEmpty(charge.FailureCode)
                ? charge.FailureCode
                : charge.Outcome?.Reason;
            var cardKey = charge.PaymentMethodDetails?.Card?.Fingerprint
                ?? charge.CustomerId;

            results.Add(new FailedCharge(
                declineCode,
                charge.Amount,
                charge.Currency,
                charge.Created,
                cardKey));

            if (results.Count >= maxCharges) break;
        }

        return results;
    }
}
