# Review preparation checklist

App id `com.recoverflow.dashboard`. What must be true before Bruce clicks Submit.
Requirements per Stripe's publishing guide and review requirements, as recorded in
the research file on 12 August 2026.

<!-- Sources: https://docs.stripe.com/stripe-apps/publish-app,
     https://docs.stripe.com/stripe-apps/review-requirements,
     https://docs.stripe.com/stripe-apps/test-app -->

---

## 0. The gate before everything: account strategy

- [ ] **Resolve the Connect platform publishing blocker.** On 2026-08-04,
      `stripe apps upload` from acct_1Trk3HLjCE5YmPhU was refused: a Connect platform
      account cannot choose public distribution. Either Stripe lifts the restriction
      (support email drafted in `stripe-app/STRIPE_SUPPORT_EMAIL.md`, never sent) or a
      separate non-Connect Stripe account owns and publishes the app
      (`stripe-app/BLOCKED.md` Option 3). Nothing below ships until this is settled.
      <!-- Source: stripe-app/BLOCKED.md lines 1-22, per the codebase recon. -->
- [ ] **One app per account.** Publishing this app forecloses publishing the Retry
      Waste Audit app from the same account. Decide deliberately.
      <!-- Source: https://docs.stripe.com/stripe-apps/publish-app; the tradeoff is
           argued in stripe-app/LISTING.md "Strategic note". -->
- [ ] **App id is final before first upload.** `com.recoverflow.dashboard` is globally
      unique and unchangeable after the first upload.
- [ ] First upload also generates the `absec_` signing secret. Retrieve it from
      Dashboard > Apps > overflow menu and set it as a Render env var (`sync: false`,
      like the other secrets in render.yaml). The backend config ships with it empty
      until then.
      <!-- Source: https://docs.stripe.com/stripe-apps/build-backend; render.yaml
           lines 25-64 pattern. -->
- [ ] **Bruce runs `stripe apps upload` himself.** No CLI upload happens from this
      workstream.

## 1. Reviewer test account

Stripe does not permit real accounts for review, and 2FA-locked credentials are a
named common rejection reason.

- [ ] Create a **dedicated reviewer merchant account** on RecoverFlow (not Bruce's
      own, not a customer's).
- [ ] **Seed test data**: connect a Stripe test account with failed invoices covering
      the states the views render. Use decline test cards so cases land in different
      classifications, e.g. `4000000000009995` (insufficient_funds, soft decline that
      retries) and `4000000000000002` (generic_decline). Seed at least one case with
      dunning emails sent so the progress view has something to show, and one
      recovered case so the overview stats are non-zero.
      <!-- Decline classification: src/RecoverFlow.Domain/Services/
           DeclineCodeClassifier.cs. Test cards: https://docs.stripe.com/testing;
           re-verify codes against that page before submitting. -->
- [ ] Credentials are for the **highest role** the product has.
- [ ] **MFA disabled** on the reviewer account, or step-by-step instructions provided.
      Note: RecoverFlow sign-in is a passwordless magic link to email
      (src/RecoverFlow.Api/Controllers/AuthController.cs), so the credentials table
      must include a mailbox the reviewer can read, or a documented bypass for review.
      Decide which before submission; a magic-link-only login with no reviewer mailbox
      is functionally a locked account.
- [ ] Include any required data files with the submission.

## 2. Testing guidance (required listing field)

- [ ] Written walkthrough covering **all three key features** in production, including
      onboarding: install, connect at recoverflow.org, run the free scan, then view a
      customer / invoice / subscription page and the overview drawer.
- [ ] Cover both sandbox and live mode, and more than one user role, since reviewers
      test both.
- [ ] Screen recordings of the install-and-connect flow attached (recommended by
      Stripe to expedite review; the install-from-partner flow counts as complex).

## 3. URLs to submit

| Field | URL | Status in repo |
|---|---|---|
| Pricing page (mandatory, paid app) | `https://recoverflow.org/pricing/` | Exists: `docs/pricing/index.html` |
| Privacy policy (required) | `https://recoverflow.org/privacy/` | Exists: `docs/privacy/index.html` |
| Terms of service (optional) | `https://recoverflow.org/terms/` | Exists: `docs/terms/index.html` |
| Company website (optional) | `https://recoverflow.org/` | Exists: `docs/index.html` |
| Support channel | `admin@recoverflow.org`, response within one business day | Matches site footer |

- [ ] **Privacy policy action item:** the page exists, but it predates this embedded
      view. Confirm it discloses what the Dashboard view reads (customers, invoices,
      subscriptions, charges, payment intents) and the dunning email practices,
      including unsubscribe handling, before submission. The old audit app had its own
      privacy section written for it; this app needs the same treatment.
      <!-- Dunning/consent risk flagged in the research file: the App Marketplace
           Agreement bars apps designed to send commercial messages without consent;
           position dunning as transactional recovery and disclose it in the policy.
           Sources: https://stripe.com/legal/app-marketplace-agreement,
           https://docs.stripe.com/payments/checkout/compliant-promotional-emails -->

## 4. Manifest and code checks

- [ ] `distribution_type` is `public` (the decided distribution).
- [ ] All five permissions carry the purpose strings from PERMISSIONS.md.
- [ ] `content_security_policy.connect-src` lists the backend URL with a path or
      trailing slash (e.g. `https://api.recoverflow.org/app/`), HTTPS only, and never
      a Stripe API URL.
- [ ] `sandbox_install_compatible: true` so reviewers can install into a sandbox.
      Every competitor listing surveyed shows sandbox testing unavailable, so this is
      also a small differentiator.
- [ ] No localhost or dummy URLs anywhere in the manifest; no hard-coded API keys in
      the bundle (both are named common rejection reasons).
- [ ] Backend endpoints for the app verify the `Stripe-Signature` header
      (payload `{user_id, account_id}`, in that order) and answer OPTIONS preflights
      with `Access-Control-Allow-Origin: *` on the authenticated endpoints only.
- [ ] Icon matches the manifest logo, min 300x300, square, PNG/JPG, under 10MB.
- [ ] Any upgrade or subscribe button routes to recoverflow.org first, never straight
      to a Stripe Checkout.
- [ ] US-style date and time formatting in all views; loading and error states
      handled; no ads.

## 5. Pre-submission sequence

1. External test round: upload, then Dashboard > Apps > External test tab, invite up
   to 25 admin testers (public-distribution apps only).
2. Fix findings; releases are fixed-forward, so version bumps only.
3. Set distribution public, submit with the required listing fields from LISTING.md
   and the screenshots from SCREENSHOTS.md.
4. Expect a first response in about 4 business days. Any app or listing change after
   approval forces resubmission and re-review, and any permission change prompts every
   installed user to reauthorize.
