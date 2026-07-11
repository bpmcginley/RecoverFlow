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

    // Real SendGrid keys always start with "SG." — anything else (empty, placeholder)
    // means this environment has no mail capability and should still boot.
    private bool IsConfigured => _email.ApiKey.StartsWith("SG.", StringComparison.Ordinal);

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
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
            plainTextContent: null,
            htmlContent: htmlBody);

        var response = await new SendGridClient(_email.ApiKey).SendEmailAsync(message, ct);
        if (!response.IsSuccessStatusCode)
            log.LogError("SendGrid returned {StatusCode} sending \"{Subject}\" to {To}",
                response.StatusCode, subject, to);
    }
}
