using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Recovery;
using SendGrid.Helpers.EventWebhook;

namespace RecoverFlow.Api.Controllers;

[ApiController]
[Route("webhooks")]
public sealed class SendGridWebhookController(
    IBackgroundJobClient jobs,
    IOptions<EmailOptions> emailOptions,
    ILogger<SendGridWebhookController> log) : ControllerBase
{
    /// <summary>
    /// Receives SendGrid event-webhook batches (opens/clicks on dunning mail). Verifies the
    /// ECDSA signature, then hands the batch to a background job so SendGrid gets its 200
    /// immediately. While no verification key is configured the endpoint answers 503 rather
    /// than accepting unverified events.
    /// </summary>
    [HttpPost("sendgrid")]
    public async Task<IActionResult> Receive()
    {
        var key = emailOptions.Value.WebhookVerificationKey;
        if (string.IsNullOrEmpty(key))
        {
            log.LogWarning("Email:WebhookVerificationKey not set — rejecting SendGrid event webhook");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var payload = await new StreamReader(Request.Body).ReadToEndAsync();

        var signature = Request.Headers[RequestValidator.SIGNATURE_HEADER].ToString();
        var timestamp = Request.Headers[RequestValidator.TIMESTAMP_HEADER].ToString();
        if (signature.Length == 0 || timestamp.Length == 0)
        {
            log.LogWarning("Rejected SendGrid webhook without signature headers");
            return BadRequest();
        }

        try
        {
            var validator = new RequestValidator();
            if (!validator.VerifySignature(validator.ConvertPublicKeyToECDSA(key), payload, signature, timestamp))
            {
                log.LogWarning("Rejected SendGrid webhook with invalid signature");
                return BadRequest();
            }
        }
        catch (Exception ex) // malformed key or signature encoding
        {
            log.LogWarning(ex, "Rejected SendGrid webhook: signature verification failed");
            return BadRequest();
        }

        jobs.Enqueue<SendGridEventProcessor>(p => p.ProcessAsync(payload));
        return Ok();
    }
}
