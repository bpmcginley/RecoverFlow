using System.Text;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecoverFlow.Api.Controllers;
using RecoverFlow.Application.Common;
using Stripe;

namespace RecoverFlow.Tests.Integration;

/// <summary>
/// Two Stripe endpoints post to /webhooks/stripe: the Connect platform's and the one on the
/// account that owns the Stripe App. Each signs with its own secret, and Stripe delivers an
/// uninstall notice only through the app account's, so a controller that checks one secret
/// silently drops every event from an installed merchant.
/// </summary>
public sealed class StripeWebhookControllerTests
{
    private const string PlatformSecret = "whsec_platform";
    private const string AppSecret = "whsec_app";

    private const string Payload = """
        {"id":"evt_1","object":"event","api_version":"2026-07-29.dahlia","created":1756800000,
         "account":"acct_123","type":"account.application.deauthorized",
         "data":{"object":{"id":"ca_test","object":"application","name":"RecoverFlow"}}}
        """;

    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public List<Job> Created { get; } = [];

        public string Create(Job job, IState state)
        {
            Created.Add(job);
            return Created.Count.ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private static string Sign(string secret)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        return $"t={ts},v1={EventUtility.ComputeSignature(secret, ts, Payload)}";
    }

    private static async Task<(int Status, RecordingJobClient Jobs)> PostAsync(
        string? signature, string platformSecret = PlatformSecret, string appSecret = AppSecret)
    {
        var jobs = new RecordingJobClient();
        var controller = new StripeWebhookController(
            jobs,
            Options.Create(new StripeOptions { WebhookSecret = platformSecret, AppWebhookSecret = appSecret }),
            NullLogger<StripeWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Payload));
        if (signature is not null) controller.Request.Headers["Stripe-Signature"] = signature;

        var result = await controller.Receive();
        return (((StatusCodeResult)result).StatusCode, jobs);
    }

    [Fact]
    public async Task A_delivery_signed_by_the_platform_endpoint_is_accepted()
    {
        var (status, jobs) = await PostAsync(Sign(PlatformSecret));

        Assert.Equal(200, status);
        Assert.Single(jobs.Created);
    }

    [Fact]
    public async Task A_delivery_signed_by_the_app_account_endpoint_is_accepted()
    {
        var (status, jobs) = await PostAsync(Sign(AppSecret));

        Assert.Equal(200, status);
        Assert.Single(jobs.Created);
    }

    [Fact]
    public async Task A_delivery_signed_by_neither_is_rejected_and_never_queued()
    {
        var (status, jobs) = await PostAsync(Sign("whsec_someone_else"));

        Assert.Equal(400, status);
        Assert.Empty(jobs.Created);
    }

    [Fact]
    public async Task An_unset_app_secret_is_skipped_rather_than_matched()
    {
        // Before the app account's endpoint existed this option was blank. Blank must not
        // become a secret that an attacker can sign against.
        var (status, _) = await PostAsync(Sign(""), appSecret: "");

        Assert.Equal(400, status);
    }

    [Fact]
    public async Task A_missing_signature_header_is_rejected()
    {
        var (status, jobs) = await PostAsync(signature: null);

        Assert.Equal(400, status);
        Assert.Empty(jobs.Created);
    }
}
