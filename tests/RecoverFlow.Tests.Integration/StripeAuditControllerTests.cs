using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecoverFlow.Api.Controllers;
using RecoverFlow.Application.Audit;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Connect;

namespace RecoverFlow.Tests.Integration;

/// <summary>
/// Exercises the Marketplace audit flow end to end with fakes in place of Stripe and SendGrid.
/// The controller is where the flow logic lives (which entry path, what gets signed, what the
/// install URL is) and it had no coverage at all; the bugs found in this feature so far have
/// been in exactly these seams rather than in the arithmetic.
/// </summary>
public class StripeAuditControllerTests
{
    private const string AccountId = "acct_test_123";

    private sealed class FakeOAuth : IStripeOAuthClient
    {
        public string? SeenCode;
        public Task<StripeOAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken ct = default)
        {
            SeenCode = code;
            return Task.FromResult(new StripeOAuthTokenResult(AccountId, "sk_live_SECRET_TOKEN", null, "read_only"));
        }
    }

    private sealed class FakeReader(params FailedChargeAttempt[] charges) : IRetryWasteReader
    {
        public string? SeenAccountId;
        public int Calls;
        public Task<ChargeScanResult> ListFailedChargesAsync(
            string stripeAccountId, DateTime sinceUtc, int maxCharges, CancellationToken ct = default)
        {
            SeenAccountId = stripeAccountId;
            Calls++;
            return Task.FromResult(new ChargeScanResult(charges, false, sinceUtc));
        }
    }

    private sealed class RecordingEmail : IEmailSender
    {
        public readonly List<(string To, string Subject, string Html, string Text)> Sent = [];
        public Task SendAsync(string to, string subject, string html, string text,
            string? trackingId = null, CancellationToken ct = default)
        {
            Sent.Add((to, subject, html, text));
            return Task.CompletedTask;
        }
    }

    private static FailedChargeAttempt Charge(string code, long cents = 5000) =>
        new(Guid.NewGuid().ToString("N"), cents, "usd", code, "fp_1", new DateTime(2026, 6, 1));

    private static (StripeAuditController C, FakeOAuth O, FakeReader R, RecordingEmail E, IDataProtectionProvider P)
        Build(IDataProtectionProvider? shared = null, params FailedChargeAttempt[] charges)
    {
        var protection = shared ?? new EphemeralDataProtectionProvider();
        var oauth = new FakeOAuth();
        var reader = new FakeReader(charges.Length > 0 ? charges : [Charge("lost_card"), Charge("insufficient_funds")]);
        var email = new RecordingEmail();
        var controller = new StripeAuditController(
            Options.Create(new StripeOptions
            {
                ClientId = "ca_TEST",
                OAuthRedirectUri = "https://api.recoverflow.org/connect/stripe/callback",
                AuditRedirectUri = "https://api.recoverflow.org/connect/stripe/audit/callback",
            }),
            Options.Create(new BacktestOptions { WindowDays = 90 }),
            protection, oauth, reader, email,
            NullLogger<StripeAuditController>.Instance);
        return (controller, oauth, reader, email, protection);
    }

    private static string StateFrom(IActionResult r)
    {
        var url = Assert.IsType<RedirectResult>(r).Url;
        var q = new Uri(url).Query;
        var state = System.Web.HttpUtility.ParseQueryString(q)["state"];
        Assert.False(string.IsNullOrEmpty(state));
        return state!;
    }

    // --- authorize -------------------------------------------------------------------

    [Fact]
    public void Authorize_uses_connect_oauth_because_the_marketplace_is_closed_to_us()
    {
        var (c, _, _, _, _) = Build();

        var url = Assert.IsType<RedirectResult>(c.Authorize("a@b.com", "Acme")).Url;

        // This pointed at marketplace.stripe.com until 4 Aug 2026, which is correct for a
        // LISTED Stripe App. Stripe refused the upload: a Connect platform account cannot take
        // public distribution, and private distribution reaches only our own team, so there is
        // no listing to install from. Do not "fix" this back without reading stripe-app/BLOCKED.md.
        Assert.StartsWith("https://connect.stripe.com/oauth/authorize", url);
        Assert.DoesNotContain("marketplace.stripe.com", url);
    }

    [Fact]
    public void Authorize_asks_for_read_only_and_nothing_else()
    {
        var (c, _, _, _, _) = Build();

        var url = Assert.IsType<RedirectResult>(c.Authorize("a@b.com", null)).Url;

        // On the Connect flow the scope is requested explicitly rather than declared in a
        // manifest, so this string is what keeps the read-only promise the whole audit rests on.
        Assert.Contains("scope=read_only", url);
        Assert.DoesNotContain("read_write", url);
    }

    [Fact]
    public void Authorize_uses_the_audit_redirect_not_the_products()
    {
        var (c, _, _, _, _) = Build();

        var url = Assert.IsType<RedirectResult>(c.Authorize("a@b.com", null)).Url;

        Assert.Contains(Uri.EscapeDataString("https://api.recoverflow.org/connect/stripe/audit/callback"), url);
    }

    [Fact]
    public void Authorize_requires_an_email_because_the_report_has_to_go_somewhere()
    {
        var (c, _, _, _, _) = Build();

        Assert.IsType<BadRequestObjectResult>(c.Authorize("  ", null));
    }

    // --- callback, our own flow ------------------------------------------------------

    [Fact]
    public async Task Callback_with_our_state_scans_the_account_and_emails_the_report()
    {
        var (c, o, r, e, _) = Build();
        var state = StateFrom(c.Authorize("owner@acme.com", "Acme"));

        var result = await c.Callback("code_abc", state, default);

        Assert.Equal("/audit-sent.html", Assert.IsType<RedirectResult>(result).Url);
        Assert.Equal("code_abc", o.SeenCode);
        Assert.Equal(AccountId, r.SeenAccountId);
        var sent = Assert.Single(e.Sent);
        Assert.Equal("owner@acme.com", sent.To);
        Assert.Contains("Acme", sent.Text);
    }

    [Fact]
    public async Task The_access_token_never_reaches_the_reader_or_the_email()
    {
        var (c, _, r, e, _) = Build();
        var state = StateFrom(c.Authorize("owner@acme.com", "Acme"));

        await c.Callback("code_abc", state, default);

        // The reader is handed the ACCOUNT, never the token: reads go through the platform
        // key plus the Stripe-Account header. This is the claim the listing rests on.
        Assert.Equal(AccountId, r.SeenAccountId);
        var sent = Assert.Single(e.Sent);
        Assert.DoesNotContain("sk_live_SECRET_TOKEN", sent.Text);
        Assert.DoesNotContain("sk_live_SECRET_TOKEN", sent.Html);
    }

    [Fact]
    public async Task Callback_rejects_a_tampered_state()
    {
        var (c, _, _, e, _) = Build();
        var state = StateFrom(c.Authorize("owner@acme.com", null));

        var result = await c.Callback("code_abc", state + "tampered", default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(e.Sent);
    }

    [Fact]
    public async Task Callback_rejects_state_signed_by_a_different_key()
    {
        var (issuer, _, _, _, _) = Build();
        var state = StateFrom(issuer.Authorize("owner@acme.com", null));
        var (other, _, _, e, _) = Build();   // different ephemeral key ring

        Assert.IsType<BadRequestObjectResult>(await other.Callback("code_abc", state, default));
        Assert.Empty(e.Sent);
    }

    [Fact]
    public async Task Callback_without_a_code_is_rejected()
    {
        var (c, _, _, e, _) = Build();

        Assert.IsType<BadRequestResult>(await c.Callback("", "whatever", default));
        Assert.Empty(e.Sent);
    }

    // --- callback, Marketplace install (no state of ours) ----------------------------

    [Fact]
    public async Task A_marketplace_install_is_not_rejected_for_having_no_state()
    {
        var (c, _, r, e, _) = Build();

        var result = await c.Callback("code_from_stripe", state: null, default);

        // Rejecting this would break every real install from the Marketplace.
        var url = Assert.IsType<RedirectResult>(result).Url;
        Assert.StartsWith("/audit-email.html?t=", url);
        Assert.Empty(e.Sent);            // nothing sent yet: no address to send to
        Assert.Equal(0, r.Calls);        // and no scan until someone asks for one
    }

    [Fact]
    public async Task The_post_install_ticket_carries_no_access_token()
    {
        var (c, _, _, _, _) = Build();

        var url = Assert.IsType<RedirectResult>(await c.Callback("code", null, default)).Url;

        Assert.DoesNotContain("sk_live_SECRET_TOKEN", Uri.UnescapeDataString(url));
    }

    [Fact]
    public async Task Run_completes_a_marketplace_install_and_emails_the_report()
    {
        var shared = new EphemeralDataProtectionProvider();
        var (c, _, r, e, _) = Build(shared);
        var url = Assert.IsType<RedirectResult>(await c.Callback("code", null, default)).Url;
        var ticket = Uri.UnescapeDataString(url["/audit-email.html?t=".Length..]);

        var result = await c.Run(ticket, "installer@acme.com", default);

        Assert.Equal("/audit-sent.html", Assert.IsType<RedirectResult>(result).Url);
        Assert.Equal(AccountId, r.SeenAccountId);
        Assert.Equal("installer@acme.com", Assert.Single(e.Sent).To);
    }

    [Fact]
    public async Task Run_rejects_a_tampered_ticket()
    {
        var (c, _, _, e, _) = Build();
        var url = Assert.IsType<RedirectResult>(await c.Callback("code", null, default)).Url;
        var ticket = Uri.UnescapeDataString(url["/audit-email.html?t=".Length..]);

        Assert.IsType<BadRequestObjectResult>(await c.Run(ticket + "x", "a@b.com", default));
        Assert.Empty(e.Sent);
    }

    [Fact]
    public async Task Run_requires_both_a_ticket_and_an_address()
    {
        var (c, _, _, e, _) = Build();

        Assert.IsType<BadRequestObjectResult>(await c.Run("", "a@b.com", default));
        Assert.IsType<BadRequestObjectResult>(await c.Run("t", "  ", default));
        Assert.Empty(e.Sent);
    }

    [Fact]
    public async Task A_ticket_cannot_be_replayed_as_oauth_state()
    {
        var (c, _, _, e, _) = Build();
        var url = Assert.IsType<RedirectResult>(await c.Callback("code", null, default)).Url;
        var ticket = Uri.UnescapeDataString(url["/audit-email.html?t=".Length..]);

        // Distinct protector purposes mean the two signed blobs are not interchangeable.
        Assert.IsType<BadRequestObjectResult>(await c.Callback("code2", ticket, default));
        Assert.Empty(e.Sent);
    }

    // --- the report the flow actually produces ---------------------------------------

    [Fact]
    public async Task A_clean_account_is_told_to_buy_nothing()
    {
        var soft = Enumerable.Range(0, 20).Select(_ => Charge("insufficient_funds")).ToArray();
        var (c, _, _, e, _) = Build(null, soft);
        var state = StateFrom(c.Authorize("owner@acme.com", null));

        await c.Callback("code", state, default);

        Assert.Contains("should not buy", Assert.Single(e.Sent).Text);
    }
}
