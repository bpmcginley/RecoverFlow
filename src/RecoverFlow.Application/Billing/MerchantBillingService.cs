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
/// Monthly billing: 25% of attributably-recovered revenue, trimmed to the published monthly cap
/// (never waived) and raised to the monthly minimum by a top-up (waived during the trial window).
/// Recoveries later refunded or charged back are credited back off the whole bill, minimum
/// included, and a credit too large for one month finishes on the next rather than being written
/// off. Crash-safety order per merchant: reserve the
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
                && p.FeeInvoiceId == null
                // A recovery handed straight back is not a recovery. Fully reversed cases drop
                // out here and are never billed at all; partial ones bill on what's left.
                && p.ReversedAmountCents < p.AmountCents)
            .ToListAsync(ct);

        // v1 bills USD only; other currencies stay unstamped so nothing is silently swallowed.
        var (usd, other) = (unbilled.Where(Usd).ToList(), unbilled.Count(p => !Usd(p)));
        if (other > 0)
            log.LogWarning("Merchant {MerchantId} has {Count} non-USD recoveries left unbilled", merchant.Id, other);

        var baseCents = usd.Sum(NetRecovered);
        var feeCents = baseCents * _opts.FeeBasisPoints / 10_000; // integer division rounds down, in the merchant's favor
        var inTrial = merchant.CreatedAt.AddDays(_opts.TrialDays) > now;
        var floorTopUp = inTrial ? 0 : Math.Max(0, _opts.MonthlyMinimumCents - feeCents);

        // The published monthly ceiling. It binds far above the floor, so the excess always
        // comes off the percentage line and the two lines still sum to the total. Trimming
        // here rather than at send time means the stored invoice matches what we charged.
        feeCents -= Math.Max(0, feeCents + floorTopUp - _opts.MonthlyCapCents);

        // Cap first, then credit: the ceiling is on what we charge for the month, and taking the
        // credit off afterwards can only lower the bill further. The credit comes off the whole
        // bill, minimum included — held back to the fee line it would strand indefinitely on a
        // merchant whose quiet months are all floor. Whatever this month can't absorb stays owed
        // on the case and comes off the next invoice.
        var grossTotal = feeCents + floorTopUp;
        var credits = await PlanReversalCreditsAsync(merchant, grossTotal, ct);
        var creditCents = credits.Sum(c => c.Cents);
        var total = grossTotal - creditCents;

        // Post-trial merchants with zero recoveries still owe the floor — that's the deal.
        // Sub-50¢ totals can't be collected by Stripe; those cases roll into next month.
        if (total < _opts.MinimumInvoiceableCents && creditCents == 0) return;

        // A credit that swallowed the whole bill leaves nothing for Stripe to collect, but the
        // month still happened: it is recorded as a settled invoice so the credit is spent once
        // and the cases it paid for are stamped. Any last few cents under Stripe's minimum go
        // uncollected rather than carried, which is the merchant's way.
        var settledByCredit = total < _opts.MinimumInvoiceableCents;
        if (!settledByCredit) await EnsurePlatformCustomerAsync(merchant, ct);

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
            ReversalCreditCents = creditCents,
            FloorTopUpCents = floorTopUp,
            TotalCents = total,
            Status = settledByCredit ? FeeInvoiceStatus.Sent : FeeInvoiceStatus.Pending,
            SentAtUtc = settledByCredit ? now : null,
            CreatedAtUtc = now,
        };
        db.FeeInvoices.Add(invoice);
        foreach (var p in usd)
        {
            p.FeeInvoiceId = invoice.Id;
            // What this case contributed, and its share of the fee actually charged. Stamped now
            // so that if it is reversed later the credit is computed from what we took, not from
            // a fresh 25% — which after a capped month would hand back more than we ever charged.
            p.BilledBaseCents = NetRecovered(p);
            p.BilledFeeCents = 0;
        }
        AllocateFee(usd, feeCents, baseCents);
        // Stamping the credits in the same SaveChanges is what makes them one-shot: a crash after
        // this resumes the same invoice, and the cases are already marked as credited on it.
        foreach (var (payment, cents) in credits) payment.ReversalCreditedCents += cents;
        if (settledByCredit) foreach (var p in usd) p.BilledAtUtc = now;
        await db.SaveChangesAsync(ct);

        if (settledByCredit)
        {
            log.LogInformation(
                "Merchant {MerchantId} owes nothing for {Period}: {Credit} of reversal credit covered the bill",
                merchant.Id, invoice.PeriodLabel, creditCents);
            return;
        }

        var result = await invoicer.SendFeeInvoiceAsync(
            merchant.StripePlatformCustomerId!, invoice.Id, BuildLines(invoice),
            invoice.Currency, _opts.InvoiceDueDays, knownStripeInvoiceId: null, ct);
        await ApplySendResultAsync(invoice, result, now, ct);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Fee invoice {invoice.Id} failed to send: {result.Error}");
    }

    /// <summary>What is left of a recovery once refunds and chargebacks are taken off it.</summary>
    private static long NetRecovered(FailedPayment p) => Math.Max(0, p.AmountCents - p.ReversedAmountCents);

    /// <summary>
    /// Splits the month's fee across the cases that earned it, so each case carries the share we
    /// actually charged for it. The remainder from integer division goes on the last case, which
    /// keeps the shares summing to the fee exactly — a share that drifted high would let a later
    /// reversal credit back more than the invoice ever collected.
    /// </summary>
    private static void AllocateFee(List<FailedPayment> cases, long feeCents, long baseCents)
    {
        if (baseCents <= 0 || cases.Count == 0) return;
        long allocated = 0;
        for (var i = 0; i < cases.Count - 1; i++)
        {
            var share = feeCents * cases[i].BilledBaseCents / baseCents;
            cases[i].BilledFeeCents = share;
            allocated += share;
        }
        cases[^1].BilledFeeCents = feeCents - allocated;
    }

    /// <summary>
    /// The fee still owed back on a case that was billed and then reversed. Reversals that landed
    /// before the case was billed are excluded: they already came off the invoice, and crediting
    /// them again would refund a fee that was never charged.
    /// </summary>
    private static long CreditOwed(FailedPayment p)
    {
        if (p.BilledBaseCents <= 0) return 0;
        var reversedBeforeBilling = p.AmountCents - p.BilledBaseCents;
        var creditable = Math.Clamp(p.ReversedAmountCents - reversedBeforeBilling, 0, p.BilledBaseCents);
        // Rounded up, so the rounding cent goes to the merchant rather than to us.
        var owed = (p.BilledFeeCents * creditable + p.BilledBaseCents - 1) / p.BilledBaseCents;
        return Math.Max(0, owed - p.ReversalCreditedCents);
    }

    /// <summary>
    /// Works out which reversed cases get credited on this invoice and by how much, oldest reversal
    /// first, stopping at the month's fee. Nothing is written here: the caller stamps the result in
    /// the same SaveChanges as the invoice, so a credit is never applied twice or lost to a crash.
    /// </summary>
    private async Task<List<(FailedPayment Payment, long Cents)>> PlanReversalCreditsAsync(
        Merchant merchant, long available, CancellationToken ct)
    {
        var plan = new List<(FailedPayment, long)>();
        if (available <= 0) return plan;

        var reversed = await db.FailedPayments
            .Where(p => p.MerchantId == merchant.Id
                && p.BilledAtUtc != null
                && p.ReversedAmountCents > 0
                && p.ReversalCreditedCents < p.BilledFeeCents)
            .OrderBy(p => p.ReversedAtUtc)
            .ToListAsync(ct);

        foreach (var p in reversed.Where(Usd))
        {
            var owed = Math.Min(CreditOwed(p), available);
            if (owed <= 0) continue;
            plan.Add((p, owed));
            available -= owed;
            if (available == 0) break;
        }
        return plan;
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
        {
            // Whether the cap bit is derivable from the stored numbers, so no extra column is
            // needed. Say it on the invoice: a merchant should be able to see the ceiling work.
            var uncapped = invoice.BillableRecoveredCents * _opts.FeeBasisPoints / 10_000;
            var capped = uncapped > invoice.FeeCents
                ? $", capped at ${_opts.MonthlyCapCents / 100m:0.##}/mo (would have been ${uncapped / 100m:N2})"
                : "";
            lines.Add(new(invoice.FeeCents,
                $"{_opts.FeeBasisPoints / 100m:0.##}% performance fee on ${invoice.BillableRecoveredCents / 100m:N2} " +
                $"recovered ({invoice.RecoveredCaseCount} payment{(invoice.RecoveredCaseCount == 1 ? "" : "s")}){capped}"));
        }
        if (invoice.ReversalCreditCents > 0)
            lines.Add(new(-invoice.ReversalCreditCents,
                "Credit for recoveries later refunded or charged back"));
        if (invoice.FloorTopUpCents > 0)
            lines.Add(new(invoice.FloorTopUpCents,
                $"Monthly minimum top-up (${_opts.MonthlyMinimumCents / 100m:0.##}/mo)"));
        return lines;
    }

    private static bool Usd(FailedPayment p) => string.Equals(p.Currency, "usd", StringComparison.OrdinalIgnoreCase);
}
