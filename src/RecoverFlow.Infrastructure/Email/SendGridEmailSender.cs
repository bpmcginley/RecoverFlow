using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Recovery;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace RecoverFlow.Infrastructure.Email;

public sealed class SendGridEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SendGridEmailSender> log) : IEmailSender
{
    private readonly EmailOptions _email = options.Value;

    // Real SendGrid keys look like "SG.<22 chars>.<43 chars>" (~69 chars). The "SG."
    // prefix alone isn't enough to trust — the stock placeholder "SG.replace_me" clears
    // it and then 401s on every send, so also require a real-length key. Anything shorter
    // (empty, placeholder) means this environment has no mail capability and just skips.
    private bool HasApiKey =>
        _email.ApiKey.StartsWith("SG.", StringComparison.Ordinal) && _email.ApiKey.Length >= 50;

    // A real key with no FromAddress is a misconfigured deployment, not a mail-less
    // environment, so it is worth its own message. SendGrid answers an empty From with a 400
    // that the send path below only logs, which would leave dunning mail, audit reports and
    // sign-in links all failing quietly with nothing thrown and nothing retried.
    private bool HasSender => !string.IsNullOrWhiteSpace(_email.FromAddress);

    public async Task SendAsync(string to, string subject, string htmlBody, string plainTextBody,
        string? trackingId = null, CancellationToken ct = default)
    {
        if (!HasApiKey)
        {
            log.LogWarning("Email:ApiKey missing or placeholder — skipping \"{Subject}\" to {To}", subject, to);
            return;
        }

        if (!HasSender)
        {
            log.LogError(
                "Email:FromAddress is not set — skipping \"{Subject}\" to {To}. SendGrid rejects an empty " +
                "sender, so every message would fail until this is configured.", subject, to);
            return;
        }

        var message = MailHelper.CreateSingleEmail(
            new EmailAddress(_email.FromAddress, _email.FromName),
            new EmailAddress(to),
            subject,
            plainTextContent: plainTextBody,
            htmlContent: htmlBody);

        // Tracking only where we can attribute it: dunning mail passes a trackingId (the
        // EmailSequenceEntry id), which rides along as a custom arg so the event webhook can
        // write opened_at/clicked_at. Everything else (sign-in links, audit reports) keeps
        // tracking off — no pixel, no rewritten links. Click tracking stays out of the
        // plain-text part either way; rewritten bare URLs there look broken.
        if (trackingId is null)
        {
            message.SetClickTracking(false, false);
            message.SetOpenTracking(false);
        }
        else
        {
            message.SetClickTracking(true, false);
            message.SetOpenTracking(true);
            message.AddCustomArg(SendGridEventProcessor.TrackingArg, trackingId);
        }

        var response = await new SendGridClient(_email.ApiKey).SendEmailAsync(message, ct);
        if (!response.IsSuccessStatusCode)
            log.LogError("SendGrid returned {StatusCode} sending \"{Subject}\" to {To}",
                response.StatusCode, subject, to);
    }
}
