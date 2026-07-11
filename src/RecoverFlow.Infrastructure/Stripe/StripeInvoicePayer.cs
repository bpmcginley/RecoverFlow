using RecoverFlow.Application.Recovery;
using Stripe;

namespace RecoverFlow.Infrastructure.Stripe;

/// <summary>
/// Pays a merchant's invoice on their connected account: platform key
/// (StripeConfiguration.ApiKey) plus the Stripe-Account header.
/// </summary>
public sealed class StripeInvoicePayer : IStripeInvoicePayer
{
    public async Task<InvoicePayResult> PayInvoiceAsync(
        string invoiceId, string stripeAccountId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            await new InvoiceService().PayAsync(
                invoiceId,
                requestOptions: new RequestOptions
                {
                    StripeAccount = stripeAccountId,
                    IdempotencyKey = idempotencyKey,
                },
                cancellationToken: ct);
            return InvoicePayResult.Success;
        }
        catch (StripeException ex)
        {
            return InvoicePayResult.Declined(ex.StripeError?.DeclineCode ?? ex.StripeError?.Code, ex.Message);
        }
    }
}
