# Screenshot specification

Five screenshots, one per view, to capture once the views are built and running under
`stripe apps start`. Do not capture from a real merchant account.

## Hard requirements (all five)

- Minimum **1600 px wide**, PNG or JPG, under 10MB each.
- Must show the app **inside the Stripe Dashboard**, not a mockup or a marketing
  frame. Key-feature images that do not show the app in the Dashboard are a named
  review failure.
- **No real customer data.** Capture from the dedicated reviewer test account seeded
  with synthetic data (see REVIEW_PREP.md section 1). Synthetic emails and amounts
  only; nothing traceable to a real person or merchant.
- US-style date and time formatting visible in the views.
- Consistent Dashboard theme across all five so the gallery reads as one product.

<!-- Requirements source: https://docs.stripe.com/stripe-apps/publish-app (key feature
     image min 1600px wide, <=10MB, PNG/JPG, showing the app in the Stripe Dashboard
     with no real customer data; misaligned feature image vs title/description is a
     common rejection), recorded in the research file. Three of these five double as
     the key-feature images; shoot all five to the same spec so any can be promoted. -->

## The five shots

### 1. Overview drawer (`stripe.dashboard.drawer.default`)

The merchant-level recovery overview: cases by status, recovered revenue (net of
reversals), amount still at risk, and recovery rate, for all time and the last 30
days.

**Do not seed a recovery rate for this shot, and do not show one.** The earlier
version of this line asked for a rate that was "believable" and mid-range, which
means picking a number for how persuasive it looks. RecoverFlow has no customers
and no recovery rate, so any figure here is invented, and a gallery image carries
no caption the way the homepage hero does: the number travels alone. Reviewers
score this listing on Trust.

Shoot the cases-by-status pane instead, or blank the recovered-revenue and
recovery-rate tiles. Cases by status shows the same thing the drawer is for,
which is that the app tracks each failed invoice through to an outcome, and it
says nothing about how often that outcome is good. Non-zero case counts are fine.
Seeded synthetic amounts on individual cases are fine. An aggregate rate is not.

<!-- Data shown must match what the stats endpoint actually returns: DashboardStats
     (CasesByStatus, AmountRecoveredCents, AmountAtRiskCents, RecoveryRate,
     AmountReversedCents) in src/RecoverFlow.Application/Dashboard/DashboardModels.cs
     lines 33-47. -->

**Pairs with key feature 1** if a drawer shot reads better than a detail page;
otherwise use shot 2 for that slot.

### 2. Customer detail (`stripe.dashboard.customer.detail`)

A customer page with one open recovery case: status, amount, attempt count, and the
dunning progress strip. This is the "recovery status where you already work" shot.
Use a synthetic customer (e.g. `taylor@example.com`) whose case has 2 of 3 dunning
emails sent, one opened.

**Key feature 1 image.**

### 3. Invoice detail (`stripe.dashboard.invoice.detail`)

An invoice page whose failed payment has a recovery case showing retry attempts and
the decline code explanation. Ideal seed: an `insufficient_funds` decline with two
retry attempts recorded, so the timeline has content.

<!-- Decline explanation must match the classifier's actual categories:
     src/RecoverFlow.Domain/Services/DeclineCodeClassifier.cs (soft declines like
     insufficient_funds are retried; Stripe-blocked codes are not). -->

### 4. Subscription detail (`stripe.dashboard.subscription.detail`) with dunning progress

A subscription page listing the cases opened against its invoices, with the
three step email sequence visible on one case: friendly reminder sent and opened,
urgent sent, final notice pending. This is the clearest single image of dunning
progress, so make the sequence states legible at gallery size.

**Key feature 2 image.**

<!-- Sequence steps and states shown must match EmailSequenceEntry
     (src/RecoverFlow.Domain/Entities/EmailSequenceEntry.cs: SequenceStep 1..3,
     EmailType friendly_reminder/urgent/final_notice, SentAt/OpenedAt/ClickedAt). -->

### 5. Settings view (`settings`)

The app settings page showing the connection state to RecoverFlow (connected account,
link to the merchant dashboard at app.recoverflow.org, support contact). This shot
backs key feature 3's permissions claim. If the view shows a permissions summary, it
must read the same as stripe-app.json now does: two reads plus invoice_write. Any
"read access only" copy left in the app settings view is stale as of v0.0.3 and has to
be fixed in the view before this shot is recaptured, not cropped out of the shot.

**Key feature 3 image.**

## Capture notes

- Capture at a browser window wide enough that the Dashboard renders its full
  desktop layout; downscaling is fine, upscaling is not (1600 px is the floor).
- Re-capture all five on any visual change to the views: listing changes after
  approval trigger re-review, so batch screenshot updates with real releases.
- Store the final files in this folder as `shot-1-overview.png` through
  `shot-5-settings.png` so LISTING.md, the key features, and the gallery stay in
  sync.
