using Microsoft.EntityFrameworkCore;
using RecoverFlow.Application.Billing;
using RecoverFlow.Application.Common;
using Stripe;

namespace RecoverFlow.Infrastructure.Stripe;

/// <summary>
/// Bills merchants on the PLATFORM's own Stripe account (no Stripe-Account header, unlike
/// StripeInvoicePayer). SendInvoiceAsync both finalizes and emails the hosted payment link,
/// independent of the dashboard's "email finalized invoices" setting; AutoAdvance keeps
/// Stripe's overdue reminders on afterwards. In test mode Stripe does not deliver the email.
///
/// Idempotency layers: Stripe idempotency keys cover same-day retries (they expire ~24h),
/// the metadata search covers cross-run resumes, and the StripeInvoiceId is persisted the
/// moment the invoice exists so a crash between create and send resumes cleanly.
/// </summary>
public sealed class StripePlatformFeeInvoicer(IAppDbContext db) : IPlatformFeeInvoicer
{
    public async Task<PlatformCustomerResult> EnsureCustomerAsync(
        Guid merchantId, string email, string companyName, CancellationToken ct = default)
    {
        try
        {
            var customer = await new CustomerService().CreateAsync(
                new CustomerCreateOptions
                {
                    Email = email,
                    Name = companyName,
                    Description = $"RecoverFlow merchant {merchantId}",
                    Metadata = new() { ["recoverflow_merchant_id"] = merchantId.ToString() },
                },
                new RequestOptions { IdempotencyKey = $"rf-cust-{merchantId}" },
                ct);
            return PlatformCustomerResult.Success(customer.Id);
        }
        catch (StripeException ex)
        {
            return PlatformCustomerResult.Failure(ex.Message);
        }
    }

    public async Task<FeeInvoiceSendResult> SendFeeInvoiceAsync(
        string customerId,
        Guid feeInvoiceId,
        IReadOnlyList<FeeInvoiceLine> lines,
        string currency,
        int daysUntilDue,
        string? knownStripeInvoiceId,
        CancellationToken ct = default)
    {
        string? invoiceId = knownStripeInvoiceId;
        try
        {
            var invoices = new InvoiceService();
            var invoice = invoiceId is not null
                ? await invoices.GetAsync(invoiceId, cancellationToken: ct)
                : await FindOrCreateDraftAsync(invoices, customerId, feeInvoiceId, currency, daysUntilDue, ct);
            invoiceId = invoice.Id;

            // A crash between create and our SaveChanges would orphan the Stripe invoice;
            // persisting its id here (via the metadata search above on rerun) closes that gap.
            await PersistStripeInvoiceIdAsync(feeInvoiceId, invoice.Id, ct);

            // Add lines only to a fresh draft — a resumed invoice already has them.
            if (invoice.Status == "draft" && (invoice.Lines?.Data?.Count ?? 0) == 0)
                for (var i = 0; i < lines.Count; i++)
                    await new InvoiceItemService().CreateAsync(
                        new InvoiceItemCreateOptions
                        {
                            Customer = customerId,
                            Invoice = invoice.Id,
                            Amount = lines[i].AmountCents,
                            Currency = currency,
                            Description = lines[i].Description,
                        },
                        new RequestOptions { IdempotencyKey = $"rf-item-{feeInvoiceId}-{i}" },
                        ct);

            if (invoice.Status == "draft")
                invoice = await invoices.SendInvoiceAsync(
                    invoice.Id,
                    requestOptions: new RequestOptions { IdempotencyKey = $"rf-send-{feeInvoiceId}" },
                    cancellationToken: ct);

            return new FeeInvoiceSendResult(true, invoice.Id, invoice.HostedInvoiceUrl, null);
        }
        catch (StripeException ex)
        {
            return new FeeInvoiceSendResult(false, invoiceId, null, ex.Message);
        }
    }

    private static async Task<Invoice> FindOrCreateDraftAsync(
        InvoiceService invoices, string customerId, Guid feeInvoiceId, string currency, int daysUntilDue, CancellationToken ct)
    {
        // Resume guard: the invoice may exist at Stripe even when our row never learned its id.
        var existing = await invoices.SearchAsync(
            new InvoiceSearchOptions { Query = $"metadata['fee_invoice_id']:'{feeInvoiceId}'" },
            cancellationToken: ct);
        if (existing.Data.FirstOrDefault() is { } found) return found;

        return await invoices.CreateAsync(
            new InvoiceCreateOptions
            {
                Customer = customerId,
                CollectionMethod = "send_invoice",
                DaysUntilDue = daysUntilDue,
                AutoAdvance = true,
                PendingInvoiceItemsBehavior = "exclude", // never vacuum up stray pending items
                Currency = currency,
                Metadata = new() { ["fee_invoice_id"] = feeInvoiceId.ToString() },
            },
            new RequestOptions { IdempotencyKey = $"rf-inv-{feeInvoiceId}" },
            ct);
    }

    private async Task PersistStripeInvoiceIdAsync(Guid feeInvoiceId, string stripeInvoiceId, CancellationToken ct)
    {
        var row = await db.FeeInvoices.FirstOrDefaultAsync(f => f.Id == feeInvoiceId, ct);
        if (row is null || row.StripeInvoiceId == stripeInvoiceId) return;
        row.StripeInvoiceId = stripeInvoiceId;
        await db.SaveChangesAsync(ct);
    }
}
