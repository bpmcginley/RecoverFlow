# Permission justifications

App id `com.recoverflow.dashboard`. Two permissions, both read-only, each tied to a
viewport the app actually renders on. This file holds the reviewer-facing purpose
strings to submit with each permission, plus the internal verification trail tying
each one to actual product behavior.

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

The app requests **no write permissions**. All recovery actions (retries, dunning
emails, billing) run server-side through the merchant's existing RecoverFlow Connect
authorization, not through app permissions.

<!-- Server-side calls use the platform secret key plus the Stripe-Account header:
     src/RecoverFlow.Api/Program.cs line 61 and src/RecoverFlow.Infrastructure/Stripe/
     StripeInvoicePayer.cs lines 19-21, per the codebase recon in the research file. -->

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

---

## Explicitly not requested

| Permission | Why not |
|---|---|
| Any `*_write` | The view is read-only. Retries, dunning emails, and billing run through the merchant's existing RecoverFlow Connect authorization on the server, so no in-Dashboard write scope is needed. |
| `subscription_read` | The app declares no subscription.detail view and never reads subscriptions. If a subscription view ships later, adding this permission is a manifest change requiring re-review and user reauthorization. |
| `charge_read` | The extension never reads charges. Decline codes and reversal data shown in the app come from the RecoverFlow backend's own records, gathered server-side through the merchant's Connect authorization. |
| `payment_intent_read` | The app declares no payment detail view and the extension never reads payment intents. |
| `payment_method_read` | Card details are never read or shown. Card update flows happen on Stripe-hosted pages, off the app. |
| `secret_write` / Secret Store | The app stores no per-merchant secrets in the Dashboard. Backend auth uses the app signing secret held server-side as configuration. |
| `event_read` | Not needed for the read-only views. If install-lifecycle webhooks (account.application.authorized / deauthorized) are added later, this becomes a manifest change requiring re-review and user reauthorization; decide before first upload. |

<!-- Permission-scope changes after publication prompt every user to reauthorize
     (https://docs.stripe.com/stripe-apps/versions-and-releases, per the research
     file), which is why the later-addition rows are flagged here rather than
     deferred silently.

     Open question to resolve at first upload: whether the customer.detail /
     invoice.detail viewports themselves require the matching *_read permission to
     render or to receive objectContext. If they do not, even these two permissions
     could be dropped; if the upload validator or reviewer flags them, trim further
     rather than justify harder. -->
