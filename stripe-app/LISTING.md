# Marketplace listing submission

Every field Stripe asks for, filled in, with the character limits it enforces.
Requirements checked against Stripe's publishing guide and quality requirements on
**4 August 2026**. Copy these into the Dashboard listing form at submission time.

Sources:
- https://docs.stripe.com/stripe-apps/publish-app
- https://docs.stripe.com/stripe-apps/review-requirements
- https://docs.stripe.com/stripe-apps/reference/app-manifest

---

## Blockers before this can be submitted

1. **`icon.png`** — 300x300 minimum, 1:1, PNG or JPG, under 10MB. Must match the manifest path.
2. **Three key-feature images** — 1600px+ wide, under 10MB. See the warning in §Key features.
3. **Test account credentials** — see §Testing guidance for why this is unusually simple for us.

Everything else below is written and ready.

---

## Eligibility, checked

| Requirement | Status |
|---|---|
| Stripe account activated | Bruce to confirm |
| **One app per account** | Only app we intend to publish. See §Strategic note. |
| English-language listing | Yes |
| Not a prohibited/restricted business | Payment-operations SaaS, not on the list |
| No localhost or dummy URLs in `allowed_redirect_uris` | Confirmed: one HTTPS production URL |
| No hard-coded API keys | Confirmed: all secrets are environment variables |

---

## Listing fields

**Name** (35 max) — `Retry Waste Audit`
> 17 characters, measured. Contains none of the banned words: "Stripe", "app", "free", "paid".
> Must match `name` in `stripe-app.json`. It does.

**Built by** (80 max) — `RecoverFlow`
> 11 characters, measured.

**Category** — `Revenue optimization`
> Stripe may reassign and will notify. Second choice is Billing.

**Subtitle** (80 max) — `See which of your failed payments could never have been retried`
> 63 characters, measured. No keyword stuffing.

**About** (1,000 max) — 940 characters, measured:

> RecoverFlow builds payment-recovery tooling for subscription businesses on Stripe.
>
> Every failed-payment tool sells you more retries. Some of your declines cannot succeed on
> any attempt, ever, because the card is gone rather than temporarily short. Stripe will not
> send nine decline codes to the card network at all, so those retries are counted but never
> actually made.
>
> This audit reads your recent history in read-only mode and emails you a one-page report:
> how much of your failed revenue sits on codes no retry can collect, how much needs a new
> card rather than another attempt, how much is genuinely reachable by retrying, and where
> you stand against Visa's cap of 15 reattempts per card per 30 days.
>
> It requests read access only. No write permissions are requested and none are needed. If
> the report shows most of your failures are the ordinary retryable kind, it says so, and
> tells you Stripe's own Smart Retries already cover you.

**Pricing** — `Free`
> No pricing page required, since no paid subscription is required to use the audit.
> Note the requirement that pricing align with off-marketplace pricing: the audit is free
> everywhere, so there is nothing to reconcile.

**Support channel** — `admin@recoverflow.org`, response within one business day.

**Based in** — United States (Massachusetts)

**Supported languages** — English

**Privacy policy** — `https://recoverflow.org/privacy/`
> Section 3 covers this app specifically: what it reads, and that it stores nothing.

**Terms of service** (optional) — `https://recoverflow.org/terms/`

**Company website** (optional) — `https://recoverflow.org/`

**FAQ page** (optional) — `https://recoverflow.org/tools/retry-waste-calculator/`
> The browser version of the same calculation, with the four questions people actually ask.

---

## Key features

Stripe takes up to three. First must be the high-level value proposition; the rest are
detailed use cases. Title, description and image must align.

**1. Title** (80 max): `Find the retries that were never going to work`
**Description** (300 max), 261 characters, measured:
> Nine Stripe decline codes block execution entirely until a new card is attached. The retry
> schedule keeps running and the attempt counter keeps climbing, but no attempt reaches the
> card network. The report counts yours, prices them, and breaks them out by code.

**2. Title** (80 max): `Check your Visa reattempt budget`
**Description** (300 max), 218 characters, measured:
> Visa caps reattempts at 15 per card per 30 days, and Stripe blocks further attempts past
> it. The report counts the peak attempts against each card in any rolling 30-day window and
> flags the ones at or over the ceiling.

**3. Title** (80 max): `Read-only, and it says so`
**Description** (300 max), 240 characters, measured:
> Three read permissions, no write permission of any kind. The access token is not stored:
> the authorization code is exchanged only to identify the account. Running the audit does
> not create an account with us, and the report is not retained.

### Warning about the images

Stripe's guidance asks for images showing the app **in the Stripe Dashboard context**, and
explicitly forbids real customer data. This app has **no Dashboard UI** by design, so there is
no in-context screenshot to take. Plan:

- Use clean screenshots of the **emailed report** rendered against synthetic data, which is
  what the user actually receives.
- Generate the sample from `scripts/retry_waste_audit.py` or the test dump so no real merchant
  data can possibly appear.
- If review pushes back on the absence of Dashboard screenshots, that is the moment to decide
  whether a minimal Dashboard view is worth building. Do not build it speculatively.

---

## Testing guidance

Reviewers need to reach a working audit. Ours is unusually simple to test because **there is
no account to create and nothing to log into.**

Text to submit:

> This app has no user account and no login. Nothing needs to be created before testing.
>
> 1. Install the app on a Stripe test account that has some failed charges in the last 90
>    days. If the test account has none, create a few using the decline test cards below,
>    which produce the decline codes the report is built around.
> 2. On install you are redirected to a single page asking for an email address. Enter any
>    address you can read.
> 3. Submit. The audit reads the account's failed charges in read-only mode and emails a
>    one-page report to that address, usually within a minute.
>
> There is no dashboard to sign into and no second step. The report is the entire product.
>
> Useful Stripe test cards for generating the relevant declines:
> - `4000000000000002` generic decline
> - `4000000000009995` insufficient funds
> - `4000000000000069` expired card
> - `4000000000000127` incorrect CVC
>
> The report distinguishes codes Stripe will not send to the network from codes that merely
> need a new card, so an account with a mix of the above produces the most representative
> output.

**Test account credentials table** — submit as:

| Test account | Username | Password |
|---|---|---|
| RecoverFlow Retry Waste Audit | Not applicable, no login exists | Not applicable |

> Multi-factor authentication is not applicable for the same reason. Verify the test cards
> above still produce these codes before submitting, against
> https://docs.stripe.com/testing

---

## Common rejection reasons, checked against this app

| Reason | Our status |
|---|---|
| Localhost or broken links in the manifest | One production HTTPS redirect URI |
| Poor image quality or branding | **Open.** Icon and feature images not made yet. |
| Incorrect test credentials, working 2FA | No login exists; documented above |
| Hard-coded API keys | None. Secrets are environment variables. |
| Misaligned feature title/description/image | Titles and descriptions written to match the planned images |
| **Missing public OAuth link, using an external test URL** | **Fixed 4 Aug.** `/authorize` now redirects to `https://marketplace.stripe.com/oauth/v2/authorize`. It previously used the Connect OAuth endpoint, which would not have been an app install at all. |
| Checkout apps routing straight to Stripe | Not a checkout app |
| Missing or incomplete testing guidance | Written above |

---

## Two open manifest questions

Neither blocks writing the listing, and both get answered by `stripe apps upload`, which
validates the manifest before anything reaches a human reviewer.

1. **`ui_extension`.** The publishing guide says data-only apps should "leave `ui_extension: []`
   empty"; the manifest reference says the object may be omitted entirely, which is what we do.
   The wording in the guide is loose and the two are probably the same instruction. If upload
   complains, add `"ui_extension": { "views": [] }`.
2. **`version`.** Currently `0.0.1`. Consider `1.0.0` for a public listing, purely cosmetic.

---

## Strategic note: one app per account

**Stripe allows one published app per account.** That makes this a real choice, not a free
option. Publishing the audit means we cannot also publish a general "RecoverFlow" recovery app
under the same account without replacing it.

The research says this is the right way round anyway. A 26th "we recover failed payments"
listing competes with Churnkey and Churn Buster on trust and install count, which we lose on
both. The audit is the only listing in the category that claims to answer the retry-waste
question, and it doubles as the top of the funnel for the paid product. But it is worth making
the decision deliberately rather than discovering the constraint later.
