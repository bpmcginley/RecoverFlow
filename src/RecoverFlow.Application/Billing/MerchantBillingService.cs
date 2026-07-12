using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Entities;

namespace RecoverFlow.Application.Billing;

/// <summary>Thrown when at least one merchant's billing failed, so Hangfire retries the run.
/// Reruns are safe: billed cases are stamped, and pending invoices resume instead of duplicating.</summary>
public sealed class BillingRunIncompleteException(int failedMerchants)
    : Exception($"Billing failed for {failedMerchants} merchant(s); see logs. Rerun resumes them.");

/// <summary>
/// Monthly billing: 25% of attributably-recovered revenue plus a monthly-minimum top-up
/// (waived during the trial window). Crash-safety order per merchant: reserve the
/// FeeInvoice row and stamp its cases in one SaveChanges BEFORE calling Stripe; on failure
/// the cases stay reserved and the invoice is resumed on the next run — never re-billed.
/// </summary>
public sealed class MerchantBillingService(
    IAppDbContext db,
    IPlatformFeeInvoicer invoicer,
    IOptions<BillingOptions> options,
    ILogger<MerchantBillingService> log)
{
    private readonly BillingOptions _opts = options.Value;

    public async Task RunMonthlyBillingAsync(CancellationToken ct = default)
    {
        if (!_opts.Enabled)
        {
            log.LogInformation("Billing disabled — skipping monthly billing run");
            return;
        }

        var now = DateTime.UtcNow;
        var merchants = await db.Merchants.ToListAsync(ct); // background job: no tenant, sees all
        var failed = 0;

        foreach (var merchant in merchants)
        {
            try
            {
                // Resume runs even for disconnected merchants: a reserved invoice is money
                // already owed. Only *new* billing requires an active connection.
                await ResumePendingInvoicesAsync(merchant, now, ct);
                if (merchant.EncryptedStripeAccessToken is not null)
                    await BillUnbilledRecoveriesAsync(merchant, now, ct);
            }
            catch (Exception ex)
            {
                failed++;
                log.LogError(ex, "Billing failed for merchant {MerchantId}", merchant.Id);
            }
        }

        if (failed > 0) throw new BillingRunIncompleteException(failed);
    }

    private async Task ResumePendingInvoicesAsync(Merchant merchant, DateTime now, CancellationToken ct)
    {
        var stuck = await db.FeeInvoices
            .Where(f => f.MerchantId == merchant.Id
                && (f.Status == FeeInvoiceStatus.Pending || f.Status == FeeInvoiceStatus.Failed))
            .ToListAsync(ct);

        foreach (var invoice in stuck)
        {
            await EnsurePlatformCustomerAsync(merchant, ct);
            var result = await invoicer.SendFeeInvoiceAsync(
                merchant.StripePlatformCustomerId!, invoice.Id, BuildLines(invoice),
                invoice.Currency, _opts.InvoiceDueDays, invoice.StripeInvoiceId, ct);
            await ApplySendResultAsync(invoice, result, now, ct);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Resume of fee invoice {invoice.Id} failed: {result.Error}");
        }
    }

    private async Task BillUnbilledRecoveriesAsync(Merchant merchant, DateTime now, CancellationToken ct)
    {
        var unbilled = await db.FailedPayments
            .Where(p => p.MerchantId == merchant.Id
                && p.Status == RecoveryStatus.Recovered
                && p.RecoveryMethod != RecoveryMethod.Unknown
                && p.FeeInvoiceId == null)
            .ToListAsync(ct);

        // v1 bills USD only; other currencies stay unstamped so nothing is silently swallowed.
        var (usd, other) = (unbilled.Where(Usd).ToList(), unbilled.Count(p => !Usd(p)));
        if (other > 0)
            log.LogWarning("Merchant {MerchantId} has {Count} non-USD recoveries left unbilled", merchant.Id, other);

        var baseCents = usd.Sum(p => p.AmountCents);
        var feeCents = baseCents * _opts.FeeBasisPoints / 10_000; // integer division rounds down, in the merchant's favor
        var inTrial = merchant.CreatedAt.AddDays(_opts.TrialDays) > now;
        var floorTopUp = inTrial ? 0 : Math.Max(0, _opts.MonthlyMinimumCents - feeCents);
        var total = feeCents + floorTopUp;

        // Post-trial merchants with zero recoveries still owe the floor — that's the deal.
        // Sub-50¢ totals can't be collected by Stripe; those cases roll into next month.
        if (total < _opts.MinimumInvoiceableCents) return;

        await EnsurePlatformCustomerAsync(merchant, ct);

        // Reserve before Stripe: the invoice row and its case stamps land atomically, so a
        // crash after this point resumes this exact invoice instead of billing the cases twice.
        var invoice = new FeeInvoice
        {
            Id = Guid.NewGuid(),
            MerchantId = merchant.Id,
            PeriodLabel = now.ToString("yyyy-MM"),
            BillableRecoveredCents = baseCents,
            RecoveredCaseCount = usd.Count,
            FeeCents = feeCents,
            FloorTopUpCents = floorTopUp,
            TotalCents = total,
            Status = FeeInvoiceStatus.Pending,
            CreatedAtUtc = now,
        };
        db.FeeInvoices.Add(invoice);
        foreach (var p in usd) p.FeeInvoiceId = invoice.Id;
        await db.SaveChangesAsync(ct);

        var result = await invoicer.SendFeeInvoiceAsync(
            merchant.StripePlatformCustomerId!, invoice.Id, BuildLines(invoice),
            invoice.Currency, _opts.InvoiceDueDays, knownStripeInvoiceId: null, ct);
        await ApplySendResultAsync(invoice, result, now, ct);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Fee invoice {invoice.Id} failed to send: {result.Error}");
    }

    private async Task EnsurePlatformCustomerAsync(Merchant merchant, CancellationToken ct)
    {
        if (merchant.StripePlatformCustomerId is not null) return;

        var result = await invoicer.EnsureCustomerAsync(merchant.Id, merchant.Email, merchant.CompanyName, ct);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Platform customer creation failed: {result.Error}");

        merchant.StripePlatformCustomerId = result.CustomerId;
        await db.SaveChangesAsync(ct); // persist before invoicing so a crash doesn't orphan the customer
    }

    private async Task ApplySendResultAsync(FeeInvoice invoice, FeeInvoiceSendResult result, DateTime now, CancellationToken ct)
    {
        invoice.StripeInvoiceId = result.StripeInvoiceId ?? invoice.StripeInvoiceId;
        if (result.Succeeded)
        {
            invoice.Status = FeeInvoiceStatus.Sent;
            invoice.HostedInvoiceUrl = result.HostedInvoiceUrl;
            invoice.SentAtUtc = now;
            invoice.FailureReason = null;
            var cases = await db.FailedPayments.Where(p => p.FeeInvoiceId == invoice.Id).ToListAsync(ct);
            foreach (var p in cases) p.BilledAtUtc = now;
        }
        else
        {
            invoice.Status = FeeInvoiceStatus.Failed;
            invoice.FailureReason = result.Error;
        }
        await db.SaveChangesAsync(ct);
    }

    private List<FeeInvoiceLine> BuildLines(FeeInvoice invoice)
    {
        var lines = new List<FeeInvoiceLine>();
        if (invoice.FeeCents > 0)
            lines.Add(new(invoice.FeeCents,
                $"{_opts.FeeBasisPoints / 100m:0.##}% performance fee on ${invoice.BillableRecoveredCents / 100m:N2} " +
                $"recovered ({invoice.RecoveredCaseCount} payment{(invoice.RecoveredCaseCount == 1 ? "" : "s")})"));
        if (invoice.FloorTopUpCents > 0)
            lines.Add(new(invoice.FloorTopUpCents,
                $"Monthly minimum top-up (${_opts.MonthlyMinimumCents / 100m:0.##}/mo)"));
        return lines;
    }

    private static bool Usd(FailedPayment p) => string.Equals(p.Currency, "usd", StringComparison.OrdinalIgnoreCase);
}
