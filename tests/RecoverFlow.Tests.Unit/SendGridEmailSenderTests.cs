using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Infrastructure.Email;

namespace RecoverFlow.Tests.Unit;

public class SendGridEmailSenderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("placeholder")]
    [InlineData("your-sendgrid-api-key")]
    public async Task SendAsync_no_ops_when_api_key_missing_or_placeholder(string apiKey)
    {
        var sender = new SendGridEmailSender(
            Options.Create(new EmailOptions { ApiKey = apiKey, FromAddress = "billing@example.com" }),
            NullLogger<SendGridEmailSender>.Instance);

        // Must complete without throwing and without touching the network.
        await sender.SendAsync("customer@example.com", "subject", "<p>body</p>");
    }
}
