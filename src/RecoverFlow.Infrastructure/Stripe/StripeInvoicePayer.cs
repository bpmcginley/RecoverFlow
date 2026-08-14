using RecoverFlow.Application.Recovery;
using Stripe;

namespace RecoverFlow.Infrastructure.Stripe;

/// <summary>
/// Pays a merchant's invoice as the installed Stripe App, authenticating with the merchant's
/// own OAuth access token rather than the platform key plus a Stripe-Account header. This is
/// the one write RecoverFlow makes on a merchant's account, and the invoice_write permission
/// in stripe-app.json is what allows it.
/// </summary>
public sealed class StripeInvoicePayer : IStripeInvoicePayer
{
    public async Task<InvoicePayResult> PayInvoiceAsync(
        string invoiceId, string accessToken, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            await new InvoiceService().PayAsync(
                invoiceId,
                requestOptions: new RequestOptions
                {
                    ApiKey = accessToken,
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
