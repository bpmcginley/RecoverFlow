# Permission justifications

App id `com.recoverflow.dashboard`. Three permissions: two reads tied to viewports the
app renders on, and one write tied to the single server-side call RecoverFlow makes on
a merchant's account. This file holds the reviewer-facing purpose strings to submit
with each permission, plus the internal verification trail tying each one to actual
product behavior.

<!-- Stripe requires a user-facing "purpose" per permission, granted via
     `stripe apps grant permission "NAME" "EXPLANATION"`; permissions without thorough
     descriptions and detailed justifications are a named common review failure.
     Sources: https://docs.stripe.com/stripe-apps/reference/permissions and
     https://docs.stripe.com/stripe-apps/publish-app, recorded in the research file. -->

**How the app actually gets its data.** The UI extension makes no Stripe API calls of
its own. Every byte it displays comes from the RecoverFlow backend over `/app/v1`,
authenticated by `fetchStripeSignature`. The only Stripe data the extension consumes
is the viewport `objectContext` (the id of the customer or invoice the Dashboard user
is looking at), which it forwards to the backend to look up the matching recovery
case. The permission set was trimmed on 2026-08-13 to match that reality: an earlier
draft requested five read permissions on the RecoverIQ precedent, but three of them
(`subscription_read`, `charge_read`, `payment_intent_read`) had no corresponding view
or in-app API use, and Stripe review rejects permissions without accurate
justifications.

**What changed in v0.0.3.** Stripe rejected v0.0.2 because the install link pointed at
a Connect authorize URL rather than the Marketplace. Fixing that moved the server's
merchant access off Connect and onto this app's own OAuth grant, so the work the
backend does on a merchant's account is now governed by this permission list rather
than by a separate Connect authorization. That is why `invoice_write` appears here and
did not before: the capability is not new, only its source of authority.

<!-- The write is InvoiceService.PayAsync in src/RecoverFlow.Infrastructure/Stripe/
     StripeInvoicePayer.cs, now authenticated with RequestOptions.ApiKey set to the
     merchant's app access token. Reads: StripeHistoricalInvoiceReader.cs (invoices).
     The install link itself is StripeConnectController.Authorize. -->

Dunning emails and RecoverFlow's own billing touch no merchant Stripe data: emails go
out through SendGrid, and fee invoices are raised on RecoverFlow's own Stripe account.

---

## customer_read (Customers)

**Purpose string (matches stripe-app.json):**

> Identifies the customer you are viewing so the app can show that customer's
> RecoverFlow recovery cases and dunning email progress.

<!-- Verification: the CustomerDetail view (stripe.dashboard.customer.detail in
     stripe-app.json) reads environment.objectContext.id (cus_...) and calls
     POST /app/v1/customers/{id}/cases. Failed payments are keyed by StripeCustomerId
     and carry CustomerEmail (src/RecoverFlow.Domain/Entities/FailedPayment.cs
     lines 9-10); RecoveryCaseSummary exposes CustomerEmail
     (src/RecoverFlow.Application/Dashboard/DashboardModels.cs line 8). -->

## invoice_read (Invoices)

**Purpose string (matches stripe-app.json):**

> Identifies the invoice you are viewing so the app can show whether it has a
> RecoverFlow recovery case and how far it has progressed.

<!-- Verification: the InvoiceDetail view (stripe.dashboard.invoice.detail in
     stripe-app.json) reads environment.objectContext.id (in_...) and calls
     POST /app/v1/invoices/{id}. FailedPayment.StripeInvoiceId is the case key
     (src/RecoverFlow.Domain/Entities/FailedPayment.cs line 7, non-nullable);
     RecoveryCaseSummary.StripeInvoiceId (DashboardModels.cs line 7). -->

<!-- invoice_read also covers the server side: the free 90 day scan lists a merchant's
     open and uncollectible invoices via StripeHistoricalInvoiceReader.cs. -->

## invoice_write (Invoices)

**Purpose string (matches stripe-app.json):**

> Retries an unpaid invoice at the moment RecoverFlow judges a retry can succeed.
> RecoverFlow only ever pays an invoice you already issued; it never creates invoices
> or charges of its own.

<!-- Verification: the ONLY write RecoverFlow makes is InvoiceService.PayAsync in
     src/RecoverFlow.Infrastructure/Stripe/StripeInvoicePayer.cs. It is called from one
     place, RetryExecutionService.ExecuteAsync, against an invoice id already recorded
     on a FailedPayment, with the retry attempt id as the idempotency key. Nothing in
     the codebase creates, voids, or edits an invoice, and the purpose string above says
     so in the merchant's own terms. If that ever stops being true, this string is the
     first thing that has to change. -->

---

## Explicitly not requested

| Permission | Why not |
|---|---|
| Any other `*_write` | `invoice_write` is the whole write surface. RecoverFlow pays invoices the merchant already issued and does nothing else on their account. |
| `subscription_read` | The app declares no subscription.detail view and never reads subscriptions. If a subscription view ships later, adding this permission is a manifest change requiring re-review and user reauthorization. |
| `charge_read` | Briefly added during the v0.0.3 draft and removed again. The only code that reads charges is StripeRetryWasteReader, which serves the free public audit on its own Connect `read_only` grant, not on this app's grant. The extension itself never reads charges: decline codes and reversal data come from RecoverFlow's own records. |
| `payment_intent_read` | The app declares no payment detail view and the extension never reads payment intents. |
| `payment_method_read` | Card details are never read or shown. Card update flows happen on Stripe-hosted pages, off the app. |
| `secret_write` / Secret Store | The app stores no per-merchant secrets in the Dashboard. Backend auth uses the app signing secret held server-side as configuration. |
| `event_read` | Not needed: the views read from RecoverFlow's API, not from Stripe events. If install-lifecycle webhooks (account.application.authorized / deauthorized) are added later, this becomes a manifest change requiring re-review and user reauthorization; decide before first upload. |

<!-- Permission-scope changes after publication prompt every user to reauthorize
     (https://docs.stripe.com/stripe-apps/versions-and-releases, per the research
     file), which is why the later-addition rows are flagged here rather than
     deferred silently.

     Open question still unresolved: whether the customer.detail / invoice.detail
     viewports themselves require the matching *_read permission to render or to
     receive objectContext. invoice_read is needed regardless, because the server-side
     scan lists invoices; customer_read is the one that could still be dropped if the
     viewport does not require it. If the validator or a reviewer flags either read,
     trim rather than justify harder. invoice_write cannot be trimmed: without it the
     retry, which is the product, cannot run. -->
