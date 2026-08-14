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
    [InlineData("SG.replace_me")] // has the "SG." prefix but is the stock placeholder — must not be treated as a real key
    public async Task SendAsync_no_ops_when_api_key_missing_or_placeholder(string apiKey)
    {
        var sender = new SendGridEmailSender(
            Options.Create(new EmailOptions { ApiKey = apiKey, FromAddress = "billing@example.com" }),
            NullLogger<SendGridEmailSender>.Instance);

        // Must complete without throwing and without touching the network.
        await sender.SendAsync("customer@example.com", "subject", "<p>body</p>", "body");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_no_ops_when_from_address_missing(string fromAddress)
    {
        // A real-length key with no sender is the shape a misconfigured deploy takes: the key
        // check passes, so without its own guard this reaches SendGrid and comes back 400 on
        // every message. Must short-circuit before the network instead.
        var sender = new SendGridEmailSender(
            Options.Create(new EmailOptions
            {
                ApiKey = "SG." + new string('x', 22) + "." + new string('y', 43),
                FromAddress = fromAddress,
            }),
            NullLogger<SendGridEmailSender>.Instance);

        await sender.SendAsync("customer@example.com", "subject", "<p>body</p>", "body");
    }
}
