using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
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
    private bool IsConfigured =>
        _email.ApiKey.StartsWith("SG.", StringComparison.Ordinal) && _email.ApiKey.Length >= 50;

    public async Task SendAsync(string to, string subject, string htmlBody, string plainTextBody, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            log.LogWarning("Email:ApiKey missing or placeholder — skipping \"{Subject}\" to {To}", subject, to);
            return;
        }

        var message = MailHelper.CreateSingleEmail(
            new EmailAddress(_email.FromAddress, _email.FromName),
            new EmailAddress(to),
            subject,
            plainTextContent: plainTextBody,
            htmlContent: htmlBody);

        // Transactional dunning mail doesn't need SendGrid's tracking pixel/link-rewriting —
        // it's a spam-score deduction and the rewritten links look worse to filters.
        message.SetClickTracking(false, false);
        message.SetOpenTracking(false);

        var response = await new SendGridClient(_email.ApiKey).SendEmailAsync(message, ct);
        if (!response.IsSuccessStatusCode)
            log.LogError("SendGrid returned {StatusCode} sending \"{Subject}\" to {To}",
                response.StatusCode, subject, to);
    }
}
