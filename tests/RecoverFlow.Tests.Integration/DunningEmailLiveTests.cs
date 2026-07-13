using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Recovery;
using RecoverFlow.Infrastructure.Email;
using Xunit.Abstractions;

namespace RecoverFlow.Tests.Integration;

/// <summary>
/// Manual live pass: sends a real dunning email through the production SendGrid path
/// (SendGridEmailSender → SendGrid API) so we can confirm deliverability end-to-end
/// before trusting dunning mail in production. Sends ONLY to admin@recoverflow.org.
/// No-ops (soft skip) unless a real SendGrid key is provided via env var
/// SENDGRID_API_KEY or the file %USERPROFILE%\.rf_sendgrid_key.txt (contents: SG.xxx).
/// </summary>
public sealed class DunningEmailLiveTests(ITestOutputHelper output)
{
    // Never send anywhere else — this address is the connector-monitored inbox.
    private const string Recipient = "admin@recoverflow.org";

    private static string? ResolveKey()
    {
        var fromEnv = Environment.GetEnvironmentVariable("SENDGRID_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

        var file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rf_sendgrid_key.txt");
        return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
    }

    [Fact]
    public async Task Sends_a_real_dunning_email_to_admin_via_sendgrid()
    {
        var key = ResolveKey();
        // Mirror SendGridEmailSender.IsConfigured: a real key is "SG." + length >= 50.
        if (string.IsNullOrWhiteSpace(key)
            || !key.StartsWith("SG.", StringComparison.Ordinal)
            || key.Length < 50)
        {
            output.WriteLine(
                "SKIPPED: no SendGrid key (set SENDGRID_API_KEY or ~/.rf_sendgrid_key.txt with SG....).");
            return;
        }

        var options = Options.Create(new EmailOptions
        {
            ApiKey = key,
            FromAddress = "admin@recoverflow.org",
            FromName = "RecoverFlow",
        });
        var sender = new SendGridEmailSender(options, NullLogger<SendGridEmailSender>.Instance);

        var content = DunningEmailTemplates.ForStep(1, "Verification Test", 4200, "usd");
        output.WriteLine($"Sending \"{content.Subject}\" to {Recipient} ...");

        await sender.SendAsync(Recipient, content.Subject, content.Html, content.PlainText);

        // A non-throwing SendAsync means SendGrid accepted (2xx) or the send was skipped.
        // Errors are logged, not thrown, so record the subject for manual inbox verification.
        var report =
            $"Dunning subject: {content.Subject}\n" +
            $"Recipient:       {Recipient}\n" +
            $"From:            RecoverFlow <admin@recoverflow.org>\n";
        output.WriteLine(report);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "rf-dunning-live-result.txt"), report);
    }
}
