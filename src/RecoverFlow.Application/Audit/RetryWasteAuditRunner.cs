using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Connect;

namespace RecoverFlow.Application.Audit;

/// <summary>What the callback needs to know to redirect the merchant's browser sensibly.</summary>
public sealed record AuditOutcome(bool Delivered, int TotalCharges);

/// <summary>
/// Drives one read-only audit end to end: exchange the marketplace install code for the account
/// id, scan the account's failed charges read-only, compute the retry-waste split, and email the
/// one-page report. The merchant's access token is used only to confirm the account id and is
/// never persisted — reads go through the platform key + Stripe-Account header. Nothing about the
/// audit is stored, which is exactly what the read-only listing promises.
/// </summary>
public sealed class RetryWasteAuditRunner(
    IStripeOAuthClient oauthClient,
    IStripeAuditReader reader,
    RetryWasteAuditService audit,
    IEmailSender email,
    IOptions<AuditOptions> options,
    ILogger<RetryWasteAuditRunner> log)
{
    private readonly AuditOptions _opts = options.Value;

    public async Task<AuditOutcome> RunFromCodeAsync(string code, CancellationToken ct = default)
    {
        // The access token is deliberately not captured past this line: everything below reads
        // via the platform key + Stripe-Account header, so there is no token to retain or expire.
        var accountId = (await oauthClient.ExchangeCodeAsync(code, ct)).StripeAccountId;

        var account = await reader.GetAccountAsync(accountId, ct);
        var company = string.IsNullOrWhiteSpace(account.CompanyName) ? "your account" : account.CompanyName!;

        var since = DateTime.UtcNow.AddDays(-_opts.WindowDays);
        var charges = await reader.ListFailedChargesAsync(accountId, since, _opts.MaxCharges, ct);

        var result = audit.Compute(charges, company);
        var report = audit.BuildReport(result);

        var delivered = false;
        if (!string.IsNullOrWhiteSpace(account.Email))
        {
            await email.SendAsync(account.Email!, report.Subject, report.HtmlBody, report.PlainTextBody, ct);
            delivered = true;
        }
        else
        {
            log.LogWarning("Audit for {Account} has no email on file; report not delivered", accountId);
        }

        // Optional lead capture. A one-line notice only — never the report's card fingerprints —
        // and off unless an address is configured, so no third party sees merchant data by default.
        if (!string.IsNullOrWhiteSpace(_opts.LeadNotificationAddress))
            await email.SendAsync(
                _opts.LeadNotificationAddress,
                $"[Audit lead] {company}",
                LeadNotice(result, delivered),
                LeadNotice(result, delivered),
                ct);

        log.LogInformation(
            "Retry waste audit for account {Account}: {Total} failed charges, {Waste}% waste; report {State}",
            accountId, result.TotalCharges, result.WastePercent, delivered ? "emailed" : "undelivered");

        return new AuditOutcome(delivered, result.TotalCharges);
    }

    private static string LeadNotice(RetryWasteAuditResult r, bool delivered) =>
        $"{r.Company} ran the Retry Waste Audit. {r.TotalCharges} failed charges, {r.WastePercent}% waste. " +
        $"Report to merchant: {(delivered ? "delivered" : "no email on file")}.";
}
