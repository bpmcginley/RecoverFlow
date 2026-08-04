# Retry Waste Audit: Stripe App

A **read-only** Stripe App that reports how much of a merchant's failed revenue sits on
decline codes that no retry can ever collect.

This is deliberately NOT the full RecoverFlow product. It requests no write access at all.

## Why read-only, and why a separate app

The existing RecoverFlow OAuth connection requests write access, because retrying an invoice
requires it. Research on 30 July 2026 found that to be the single biggest conversion problem
we have: a stranger is asked to grant write-capable access to their payment account by a
website they have never heard of.

Competitors have already noticed. 15 of 45 marketplace apps request no write access at all,
and RecoverStack, Reecova and Bleedpoint merchandise that fact in their listing copy.
RecoverStack additionally states its access token expires within the hour.

So the marketplace app is scoped to three read permissions and nothing else. The full product
remains a separate, later, explicit decision for anyone who wants it.

## Why this app rather than a generic "recovery" app

Of the 25 marketplace apps that act on a failed payment:

- 25 of 25 surface metrics in the Stripe Dashboard
- 20 of 25 send dunning email
- 13 of 25 claim smarter retries
- **1 of 25** suppresses retries on codes that cannot succeed (Reecova)
- **0 of 25** track the Visa reattempt budget

Listing another "we recover failed payments" app puts us twenty-somethingth in a queue behind
Churnkey and Churn Buster. Listing the only app that tells you which of your retries were
wasted puts us first at something.

## Status

**Backend built and tested. Not submitted.**

Done:

- [x] `stripe-app.json` manifest, 3 read permissions, each with a purpose string
- [x] `GET /connect/stripe/audit/authorize` — `scope=read_only`, separate protector purpose
- [x] `GET /connect/stripe/audit/callback` — handles both our own flow (signed state carries
      the email) and a Marketplace install (no state, detours via `/audit-email.html`)
- [x] `POST /connect/stripe/audit/run` — completes a Marketplace install from a signed,
      30-minute ticket that names the account and carries no token
- [x] `IRetryWasteReader` / `StripeRetryWasteReader` — charge-level read, capped at 2,000
      charges examined, card fingerprint expanded for per-card attempt counting
- [x] `RetryWasteAuditService` — the three-way split and the rolling 30-day Visa counter
- [x] `RetryWasteReportEmail` — HTML + plain text, sent through the existing SendGrid sender
- [x] 29 unit tests covering the split, the currency scoping and the Visa window
- [x] Token never persisted (see below)

Left, and all of it needs Bruce:

- [ ] `icon.png`, 300x300 minimum, 1:1, under 10MB
- [ ] Test credentials for Stripe's reviewers, 2FA disabled
- [ ] Verify in a sandbox how `post_install_action` interacts with the OAuth redirect —
      both are set, and which one governs the post-install landing is **not confirmed**.
      Worth checking before submitting rather than spending a review cycle on it.
- [ ] Submit for review

Review takes 4 business days. No listing fee. No minimum customer count.

## On "we do not keep your access token"

Stronger than it sounds, and worth stating precisely because the listing depends on it.

The reads do not use the merchant's token at all. The authorization code is exchanged solely
to learn *which* account granted access; every subsequent read goes through the platform key
plus the `Stripe-Account` header, exactly like the rest of the codebase. The access token is
a local variable in the callback that is never written to the database, never encrypted into
a `Merchant` row, and goes out of scope when the request ends.

No `Merchant` row is created either. An audit is not a signup.

## Listing copy, draft

**Name:** Retry Waste Audit (17 chars, contains no banned word)

**Subtitle:** See which of your failed payments could never have been retried

**Category:** Revenue optimization

**About:**
> Every failed-payment tool sells you more retries. Some of your declines cannot succeed on
> any attempt, ever, because the card is gone rather than temporarily short. Stripe will not
> even send nine decline codes to the card network, so those retries are counted but never
> actually made.
>
> This audit reads your last 90 days in read-only mode and emails you a one-page report: how
> much of your failed revenue sits on codes no retry can collect, how much needs a new card
> rather than another attempt, how much is genuinely reachable by retrying, and where you
> stand against Visa's cap of 15 reattempts per card per 30 days.
>
> It requests read access only. No write permissions are requested and none are needed. If
> the report shows most of your failures are the ordinary retryable kind, it will tell you
> that Stripe's own free Smart Retries already cover you.

**Key features:**
1. *Find the retries that were never going to work.* Nine Stripe decline codes block execution
   entirely until a new card is attached. The report counts yours and prices them.
2. *Check your Visa reattempt budget.* Visa caps reattempts at 15 per card per 30 days and
   Stripe blocks further attempts past it. Almost no tool shows you where you stand.
3. *Read-only, and it says so.* Three read permissions, no writes, and the access token is
   discarded once the audit finishes.

**Support:** admin@recoverflow.org, replies within one working day.

**Pricing statement:** Free. No paid subscription is required to use this audit.

## Manifest notes

`extensions` / `ui_extension.views` is intentionally empty. Stripe documents backend-only apps
as a supported type and the manifest permits an empty views array, so no Dashboard UI is built.

Verify against the current schema before submitting:
https://docs.stripe.com/stripe-apps/reference/app-manifest
