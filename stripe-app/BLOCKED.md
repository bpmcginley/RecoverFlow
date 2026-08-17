# Why this app was not published, and what replaced it

**Superseded.** This file is about `com.recoverflow.retry-waste-audit` v0.0.1, the audit app in
this directory, and it stopped describing the current state on 4 August 2026. It is kept because
the reasoning below is still the reason the code looks the way it does, not because the
conclusion still holds.

The app that RecoverFlow is actually taking to the Marketplace is `com.recoverflow.dashboard`,
in `stripe-dashboard-app/`. It reaches a merchant's data through the Stripe App install grant
rather than Connect OAuth, which is what the blocker below was about. See commit `2a96255`.

**Read the rest as history.** For anything about the current listing, read
`stripe-dashboard-app/` and nothing in this directory.

## What happened

`stripe apps upload` from `acct_1Trk3HLjCE5YmPhU` (RecoverFlow) returned:

> `Because your account is a Connect platform, you cannot choose the public distribution at this time.`

RecoverFlow's account is a Connect platform, because that is how the main product reads a
merchant's Stripe data. Stripe will not grant public distribution to a Connect platform account.

**Private distribution is not a fallback.** Per
https://docs.stripe.com/stripe-apps/distribution-options, private means *team members of your
own Stripe account*. No external merchants, no install link. It is not a smaller listing, it is
not a listing at all.

The restriction is not documented on the distribution-options page or in the publishing guide.
The CLI's phrasing, "at this time", reads like policy rather than architecture, which is why
asking is worth doing.

## What this does and does not break

**Unaffected.** The audit itself works. The report engine, the waste split, the Visa exposure
counter, the calculator at `/tools/retry-waste-calculator`, the legal pages, and 161 unit plus
22 integration tests are all independent of how a merchant reaches the authorize URL.

**Affected.** One distribution channel. We lose Stripe's directory and the borrowed trust that
came with it, which was the main argument for building the app version rather than leaving the
audit as a manual offer.

## What was changed in response

`StripeAuditController.Authorize` now redirects to **Connect OAuth with `scope=read_only`**
instead of `marketplace.stripe.com/oauth/v2/authorize`. The marketplace URL is correct for a
listed app and leads nowhere for an unlisted one.

The read-only promise survives the change, but it moves: it used to be declared by the three
`_read` permissions in the manifest and enforced from there, and it is now the literal string
`scope=read_only` in that method. Stripe still enforces it server-side, so a bug in our code
cannot widen the grant, but the manifest is no longer the thing keeping the promise.
`Authorize_asks_for_read_only_and_nothing_else` guards it.

**Do not change that URL back without reading this file.** It looks like a regression and is not.

## The manifest is kept as-is

`stripe-app.json` still says `"distribution_type": "public"`. That is deliberate. Setting it to
private would make the upload succeed and achieve nothing, and would quietly lose the record of
what we were trying to do. The listing copy in `LISTING.md` and the assets are all still valid
and ready if the restriction is lifted.

## Options, in the order worth trying

1. **Ask Stripe.** One email, drafted in `STRIPE_SUPPORT_EMAIL.md`. The only route to an actual
   listing, and the wording of the error suggests a process may exist.
2. **Ship without the marketplace.** Done. Merchants install from recoverflow.org.
3. **A separate, non-Connect Stripe account owning the app.** Technically possible, held back
   deliberately. The audit currently reads via the platform key plus the `Stripe-Account`
   header, which is the Connect pattern; on a standalone account it would have to use the
   merchant's access token directly, which weakens the "we never use your token" claim that the
   third key feature is built on. Real work and a real tradeoff. Only worth it if 1 fails and
   the listing is judged worth the architectural cost.
