using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Recovery;
using Stripe;

namespace RecoverFlow.Api.Controllers;

[ApiController]
[Route("webhooks")]
public sealed class StripeWebhookController(
    IBackgroundJobClient jobs,
    IOptions<StripeOptions> stripeOptions,
    ILogger<StripeWebhookController> log) : ControllerBase
{
    /// <summary>
    /// Receives all Stripe webhook events. Verifies the signature, then hands the
    /// event to a background job so Stripe gets its 200 immediately.
    /// </summary>
    [HttpPost("stripe")]
    public async Task<IActionResult> Receive()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();

        var signature = Request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrEmpty(signature))
        {
            log.LogWarning("Rejected webhook without a Stripe-Signature header");
            return BadRequest();
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                signature,
                stripeOptions.Value.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            log.LogWarning(ex, "Rejected webhook with invalid Stripe signature");
            return BadRequest();
        }

        jobs.Enqueue<IStripeWebhookProcessor>(p => p.ProcessAsync(json));

        log.LogInformation("Accepted webhook {EventId} ({EventType})", stripeEvent.Id, stripeEvent.Type);
        return Ok();
    }
}
