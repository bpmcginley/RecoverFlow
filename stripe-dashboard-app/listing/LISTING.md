# Marketplace listing: RecoverFlow embedded Dashboard app

App id `com.recoverflow.dashboard`. Category, character limits, and field set checked
against Stripe's publishing guide on 12 August 2026.

<!-- Field requirements source: https://docs.stripe.com/stripe-apps/publish-app
     (name max 35 chars, no "Stripe"/"app"/"free"/"paid"; subtitle 80; About 1000;
     3 key features, title 80 / description 300; pricing page link mandatory for paid apps).
     Recorded in the research file tasks/wwruapqlo.output, "publishing" findings. -->

> Copy for the Dashboard listing form. Character counts are measured, not estimated.
> Submission itself is gated on the Connect-platform publishing blocker; see REVIEW_PREP.md.

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
> The view requests read access only. Recovery actions run through your existing
> RecoverFlow connection, and you can start with a free 90 day scan, no card required.

> 955 characters measured on the plain text (without blockquote markers, with blank
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
     - Read access only: the two read permissions in the stripe-dashboard-app manifest
       (customer_read, invoice_read); justifications in PERMISSIONS.md. -->

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

`Read access only, with retries that know when to stop`

> 53 characters.

**Description (300 max):**

> The view requests two read permissions and no write access of any kind. Retries and
> emails run through your existing RecoverFlow connection, scheduled by decline code,
> so cards Stripe will not put on the wire without new details are left alone rather
> than retried into the ground.

> 280 characters.

<!-- Verification: permission set is the two read-only scopes tied to real views
     (customer_read, invoice_read), trimmed 2026-08-13 from an earlier five-scope
     draft; see PERMISSIONS.md. Decline code behavior: src/RecoverFlow.Domain/Services/
     DeclineCodeClassifier.cs (IsBlockedByStripe, ShouldRetry). Server-side actions run
     via the platform key + Stripe-Account header: src/RecoverFlow.Infrastructure/Stripe/
     StripeInvoicePayer.cs. -->

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
