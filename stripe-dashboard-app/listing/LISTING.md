# Marketplace listing: RecoverFlow embedded Dashboard app

App id `com.recoverflow.dashboard`. Category, character limits, and field set checked
against Stripe's publishing guide on 12 August 2026.

<!-- Field requirements source: https://docs.stripe.com/stripe-apps/publish-app
     (name max 35 chars, no "Stripe"/"app"/"free"/"paid"; subtitle 80; About 1000;
     3 key features, title 80 / description 300; pricing page link mandatory for paid apps).
     Recorded in the research file tasks/wwruapqlo.output, "publishing" findings. -->

> Copy for the Dashboard listing form. Character counts are measured, not estimated.
> Nothing gates submission any more: the publish flow ran end to end for 0.0.4 on
> 17 August 2026 and stopped only at the Submit button, which is where the founder
> accepts the two Stripe agreements.

---

## Name (35 max)

`RecoverFlow`

> 11 characters. Contains none of the banned words: "Stripe", "app", "free", "paid".
> Must match `name` in the app manifest.

## Built by

`RecoverFlow`

## Category

`Revenue optimization`

<!-- Stripe's own category blurb: "Select apps that help recover failed payments...".
     Source: https://marketplace.stripe.com/categories/revenue_optimization, recorded in
     the research file's marketplace survey. Second choice: Billing. -->

## Subtitle (80 max)

`Failed payment recovery status and dunning progress, right in your Dashboard`

> 76 characters.

## About (1,000 max)

> RecoverFlow recovers failed subscription payments for businesses on Stripe. Retries
> are scheduled by decline code, so cards that cannot succeed without new details are
> not hammered with pointless attempts. A three step dunning email sequence handles the
> failures no retry can fix. Every recovery is attributed to the specific action that
> caused it, and payments that came back on their own are never billed.
>
> This view brings that work into the Dashboard you already use. On a customer or
> invoice page, see whether a failed payment has an open recovery case, how many
> retries have run, which dunning emails went out and whether they were opened or
> clicked, and what has been recovered or later reversed. An overview shows
> recovered revenue, the amount still at risk, and your recovery rate.
>
> RecoverFlow reads your customers and invoices, and pays an unpaid invoice only when
> a retry is due. It never creates invoices or charges. You can start with a free 90
> day scan, no card required.

> 986 characters measured on the plain text (without blockquote markers, with blank
> lines between paragraphs).

<!-- Claim verification:
     - Retries scheduled by decline code; blocked codes never retried:
       src/RecoverFlow.Domain/Services/DeclineCodeClassifier.cs (RetryBlockedByStripe,
       Classify, ShouldRetry).
     - Three step dunning email sequence: src/RecoverFlow.Domain/Entities/EmailSequenceEntry.cs
       (SequenceStep 1..3, EmailType friendly_reminder/urgent/final_notice) and
       src/RecoverFlow.Application/Recovery/DunningEmailService.cs;
       RetryOptions.DefaultSequenceSteps = 3 in src/RecoverFlow.Application/Common/Options.cs.
     - Attribution; self-recoveries not billed:
       src/RecoverFlow.Application/Recovery/PaymentRecoveryService.cs (AttributeRecoveryAsync,
       "Attribution per spec: our retry succeeded > card update completed > email nudged them").
     - Per-case data (status, attempts, reversal): src/RecoverFlow.Application/Dashboard/
       DashboardModels.cs (RecoveryCaseSummary); email open/click tracking:
       EmailSequenceEntry.OpenedAt/ClickedAt.
     - Overview stats (recovered, at risk, recovery rate): DashboardModels.cs
       (DashboardStats: AmountRecoveredCents, AmountAtRiskCents, RecoveryRate).
     - Free 90 day scan, no card: BacktestOptions.WindowDays = 90 in
       src/RecoverFlow.Application/Common/Options.cs and docs/pricing/index.html
       ("The 90 day backward-looking scan. That is free and does not require a card.").
     - Permission set: customer_read, invoice_read, invoice_write in the
       stripe-dashboard-app manifest; justifications in PERMISSIONS.md. invoice_write
       was added in v0.0.3 when the server moved off Connect onto Stripe Apps OAuth,
       so retries now run on the app grant rather than a separate connection. -->

## Works with

Stripe Dashboard pages: Customers and Invoices, plus an app drawer overview and a
settings view. This matches the four viewports declared in stripe-app.json
(customer.detail, invoice.detail, drawer default, settings). Finalize against the
shipped manifest before submission; do not list Subscriptions or Home unless a view
for those viewports actually ships.

## Key features (up to 3)

### 1. Title (80 max)

`Recovery status on the customer and invoice pages you already use`

> 65 characters.

**Description (300 max):**

> Open a customer or invoice and see its recovery case at a glance: current status,
> amount, retry attempts so far, and whether a recovered payment was later refunded
> or charged back. No second dashboard and no tab switching.

> 222 characters.

<!-- Verification: RecoveryCaseSummary in src/RecoverFlow.Application/Dashboard/
     DashboardModels.cs carries Status, AmountCents, AttemptCount, ReversedCents,
     ReversalReason. Cases are keyed by StripeInvoiceId / StripeCustomerId /
     StripeSubscriptionId: src/RecoverFlow.Domain/Entities/FailedPayment.cs lines 7-9. -->

### 2. Title (80 max)

`Dunning email progress for every failed payment`

> 47 characters.

**Description (300 max):**

> Each recovery case shows its three step email sequence: which reminder went out,
> when it was sent, and whether the customer opened or clicked it. You can see exactly
> where a customer is in the recovery flow before you decide to reach out yourself.

> 247 characters.

<!-- Verification: src/RecoverFlow.Domain/Entities/EmailSequenceEntry.cs (SequenceStep,
     EmailType, SentAt, OpenedAt, ClickedAt, ResultedInRecovery); entries hang off
     FailedPayment.EmailEntries (src/RecoverFlow.Domain/Entities/FailedPayment.cs line 49).
     The dunning progress view is in scope per the workstream decision. -->

### 3. Title (80 max)

`One write permission, used only to retry an invoice you already issued`

> 70 characters.

**Description (300 max):**

> RecoverFlow asks for three permissions: read your customers, read your invoices, and
> pay an unpaid one. It never creates invoices or charges. Retries are scheduled by
> decline code, so cards Stripe will not put on the wire without new details are left
> alone rather than retried into the ground.

> 293 characters.

<!-- Verification: permission set is customer_read, invoice_read and invoice_write.
     charge_read was requested during the v0.0.3 draft and removed again: the only code
     that reads charges is StripeRetryWasteReader, which serves the free audit on its own
     Connect read_only grant, not the app grant. Decline code behavior:
     src/RecoverFlow.Domain/Services/DeclineCodeClassifier.cs (IsBlockedByStripe,
     ShouldRetry). The single write is the invoice pay in
     src/RecoverFlow.Infrastructure/Stripe/StripeInvoicePayer.cs, which calls
     InvoiceService.PayAsync and nothing else. -->

## Pricing

Select: **Paid subscription required**
Pricing page (mandatory for paid apps): `https://recoverflow.org/pricing/`

Pricing text for the listing, matched to the live pricing page:

> Free 90 day scan of your recent failed payments, no card required. After that,
> 25% of the recovered revenue RecoverFlow can attribute to a specific action it took,
> with a $29 monthly minimum after your first 30 days and a $299 monthly cap. Payments
> that recover on their own are never billed.

<!-- Verification against the live pricing page copy (docs/pricing/index.html):
     "25% of what we can prove we recovered, never below $29 a month and never above
     $299"; "First 30 days: no floor at all"; "The 90 day backward-looking scan. That
     is free and does not require a card."; "Never billed for payments that came back
     on their own." And against code: BillingOptions in src/RecoverFlow.Application/
     Common/Options.cs (FeeBasisPoints = 2500, MonthlyMinimumCents = 2900,
     MonthlyCapCents = 29_900, TrialDays = 30) and the floor top-up logic in
     src/RecoverFlow.Application/Billing/MerchantBillingService.cs line 103.
     Note: the shorthand "25% capped at $299/mo" alone omits the $29 floor; the floor
     is stated here because omitting it would breach Stripe's no-hidden-fees rule
     (https://docs.stripe.com/stripe-apps/publish-app). -->

<!-- Any upgrade or subscribe button inside the app must route users to recoverflow.org
     before any Stripe Checkout, per the review guidance recorded in the research file
     (source: https://docs.stripe.com/stripe-apps/publish-app). -->

## Support channel (1-2, with response time)

`admin@recoverflow.org`, response within one business day.

<!-- Same channel and response window as the prior listing draft
     (stripe-app/LISTING.md) and the site footer (docs/pricing/index.html). -->

## Based in

United States (Massachusetts)

## Supported languages

English

## Privacy policy (required)

`https://recoverflow.org/privacy/`

<!-- Page exists in the repo at docs/privacy/index.html. See REVIEW_PREP.md for the
     action item to confirm it covers this embedded view's reads. -->

## Terms of service (optional)

`https://recoverflow.org/terms/`

## Company website (optional)

`https://recoverflow.org/`

---

# Submit for review (publish step 2)

Everything above is publish step 1, the listing itself. Step 2 is the review submission and
step 3 is the release notes. Both are recorded here because the Stripe draft is the only
other copy, and a lost draft would otherwise mean rewriting them from memory.

## App version

`0.0.4`

<!-- Selected on 17 August 2026. The publish flow warned "Version 0.0.3 is currently in
     review for Marketplace. Submitting this version will replace it and restart the review
     process", which is the intent: 0.0.3 is the version review rejected. Steps 1 and 2 of
     the form carried over from the 0.0.3 draft unchanged and were re-checked field by
     field; only the release notes below are new. -->

## Marketplace install option

`Redirect to your website`

"Install from listing" is disabled for this app. Stripe's own help text on the step reads
"For OAuth apps, this is required for authorization", and `stripe-app.json` sets
`stripe_api_access_type: oauth`.

## Marketplace install URL

`https://recoverflow.org/#start`

<!-- The install has to start on our side because /connect/stripe/authorize refuses a request
     without both email and companyName (StripeConnectController.Authorize returns
     BadRequest), and it is the homepage form that collects them:
     docs/index.html line 809, `<form id="start" ... action=".../connect/stripe/authorize">`.
     That form then redirects to marketplace.stripe.com/oauth/v2/authorize, which is the
     actual app install. -->

## Call to action

`Install from partner`

## Testing credentials

Checked: **This app doesn't require users to sign in**.

<!-- True for the reviewed surface: the four Dashboard views authenticate with the signed
     app request (StripeAppAuthMiddleware), not a password. The install collects an email and
     company name, which is not a sign-in, and the separate web app at app.recoverflow.org
     uses an emailed link rather than a password. -->

## User journey 1

**Title:** `Install RecoverFlow and connect a Stripe account`

**Instructions:**

> RecoverFlow has no password login, so there are no test credentials to supply. Installing
> the app and connecting the account are one flow: the listing sends you to recoverflow.org,
> and approving the Stripe consent screen installs the app. Please follow these steps with
> the Stripe account you want to review with, including a sandbox or test account.
>
> 1. Click Install on this listing. You are redirected to https://recoverflow.org/#start, the
>    "Connect your Stripe account" form. Enter any company name and an email address you can
>    receive mail at. No card is required.
> 2. Submit the form. You are taken to Stripe's app authorization page at
>    marketplace.stripe.com, which lists the permissions RecoverFlow asks for: read your
>    customers, read your invoices, and pay an unpaid invoice. Approve it for the Stripe
>    account you are reviewing with.
> 3. You are returned to RecoverFlow, which reads the last 90 days of that account's failed
>    payments and shows what it found. That scan is free. To return later, go to
>    https://app.recoverflow.org and request a sign-in link by email.
> 4. In the Stripe Dashboard for that same account, open Apps, then RecoverFlow, then the App
>    settings tab. It confirms "This Stripe account is connected to RecoverFlow" and shows
>    recovered revenue and recovery rate.
>
> Important: the app reads recovery data for the Stripe account it is installed in. If step 2
> is skipped, every view reads "This Stripe account is not connected to RecoverFlow yet, so
> there is no recovery data to show." That message is expected behaviour, not a failure.
>
> If you would prefer us to pre-connect an account for you, email admin@recoverflow.org and
> we will set one up.

<!-- Rewritten on 14 August 2026. The version carried over from the 0.0.2 draft said the
     account "is linked through Stripe Connect OAuth" and sent the reviewer to
     connect.stripe.com, then listed the app install as a separate later step. All three
     became false in 0.0.3 when the merchant connection moved onto the Stripe Apps grant
     (commit 2a96255); that stale install link is what Stripe rejected 0.0.2 for.
     Verification of the surviving claims: the settings text is AppSettings.tsx
     ("This Stripe account is connected to RecoverFlow", recovered revenue, recovery rate);
     the not-connected text is States.tsx line 17; the 90 day window is
     BacktestOptions.WindowDays. -->

## User journey 2

**Title:** `Check recovery status and dunning progress for a failed payment`

Carried over unchanged from the 0.0.2 draft. Re-verified against the shipped views on
14 August 2026: the status badges (Recovering, Recovered, Lost, Cancelled) are
CaseStatusBadge.tsx; the email badges (Sent, Opened, Clicked, Led to recovery) are
DunningSection.tsx lines 11-14; the drawer's Recovered (net) / At risk / Recovery rate and
case counts for Last 30 days and All time are AppOverview.tsx, as is the all-currencies-in-USD
caption and the "Open RecoverFlow" external link; "No recovery activity for this customer" is
CustomerDetail.tsx line 70.

## Contact information

Follow up email: `admin@recoverflow.org`
Security incident email: `admin@recoverflow.org`
Security phone number: left blank (optional).

# Release notes (publish step 3)

For version 0.0.4, entered in the form and saved as a draft on 17 August 2026:

> Fixes the Stripe account connection. Installing RecoverFlow from the Marketplace listing
> now completes and returns you to RecoverFlow. In the previous version the install itself
> succeeded on Stripe's side, but the final step of the connection could fail and leave the
> account unconnected.
>
> Access tokens are now refreshed on schedule, so recovery keeps running on a connected
> account rather than stopping an hour after the install.
>
> No change to the permissions RecoverFlow asks for: read your customers, read your invoices,
> and pay an unpaid invoice you already issued. It never creates invoices or charges.

<!-- These are merchant-facing, so they describe the symptom rather than the cause. The cause
     was that the app install's ac_ code was being exchanged against connect.stripe.com
     instead of api.stripe.com/v1/oauth/token, which is what app review saw as an HTTP 500:
     src/RecoverFlow.Infrastructure/Stripe/StripeOAuthClient.cs, fixed in commit f491685.
     The second paragraph covers a bug review did not report and could not have seen in a
     four-day window: the Apps token response carries no expires_in, so every token was
     stored with a null expiry, MerchantStripeTokenProvider.IsExpired read null as "never
     expires", and nothing would ever have refreshed. Both are covered by
     tests/RecoverFlow.Tests.Unit/StripeOAuthClientTests.cs. -->

For version 0.0.3:

> RecoverFlow now connects your Stripe account through a Stripe App install instead of a
> separate Stripe Connect authorization, so installing from the Marketplace is what connects
> the account.
>
> This version adds one write permission, invoice_write. RecoverFlow uses it only to pay an
> unpaid invoice you already issued, at the moment a retry is due. It never creates invoices
> or charges.
>
> The app icon is now the RecoverFlow brand mark.

Step 3 is also where Stripe attaches the Developer Agreement and Marketplace Agreement to the
Submit button. Accepting those is the founder's decision, so the draft is left saved at this
step rather than submitted.

# Open discrepancy, unresolved

The listing preview in the publish flow renders the permission block as **Customers:
Read-only** and **Invoices: Read-only**, with no write permission, and reports **"Sandbox
testing not available"**. Both contradict the shipped manifest, which declares `invoice_write`
alongside the two reads and sets `sandbox_install_compatible: true`. Checked on 14 August
2026: version 0.0.3 is the version selected in the form, it is the one Stripe shows as
uploaded and Approved, and it was uploaded after the commit that added `invoice_write`, so a
stale upload does not explain it. A full page reload reproduced it. No Stripe documentation
was found that explains either line, so the cause is genuinely unknown rather than assumed.

The listing copy describes the three permissions the manifest asks for, because that is the
claim the repository can back. If Stripe's consent screen turns out to grant only the two
reads, the retry write in StripeInvoicePayer would fail at run time and both the copy and the
manifest would need revisiting.
