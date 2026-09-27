# RecoverFlow

Subscription payment recovery for indie SaaS founders on Stripe. Detects failed invoice payments via webhooks, classifies decline codes, and schedules smart retries.

## Structure (Clean Architecture)

- `src/RecoverFlow.Domain` — entities, enums, `DeclineCodeClassifier`, `RetryScheduler`
- `src/RecoverFlow.Application` — use cases (`PaymentRecoveryService`), interfaces, DTOs
- `src/RecoverFlow.Infrastructure` — EF Core (PostgreSQL, snake_case schema), Stripe event processing, Hangfire
- `src/RecoverFlow.Api` — webhook endpoint, Program.cs, Serilog, Swagger
- `tests/` — unit + integration test projects

## Run locally

1. Start PostgreSQL and create the DB:
   ```
   createdb recoverflow
   ```
2. Apply migrations:
   ```
   dotnet ef database update --project src/RecoverFlow.Infrastructure --startup-project src/RecoverFlow.Infrastructure
   ```
3. Set your Stripe test keys in `src/RecoverFlow.Api/appsettings.json` (or user-secrets): `Stripe:SecretKey`, `Stripe:WebhookSecret`, `Stripe:ClientId`. Use a [restricted key](https://docs.stripe.com/keys/restricted-api-keys.md) (`rk_...`) scoped to only the resources RecoverFlow touches (Invoices, Subscriptions, Customers, OAuth), not a full `sk_...` secret key. Generate `Encryption:Key` with `openssl rand -base64 32` — it encrypts merchant OAuth access tokens at rest.
4. Run the API:
   ```
   dotnet run --project src/RecoverFlow.Api
   ```
5. Forward Stripe test webhooks:
   ```
   stripe listen --forward-to localhost:5000/webhooks/stripe
   stripe trigger invoice.payment_failed
   ```

## Webhook flow

`POST /webhooks/stripe` verifies the `Stripe-Signature` header (`EventUtility.ConstructEvent`), returns 200 immediately, and enqueues a Hangfire job. The job dedupes by event id (`processed_webhook_events`), then routes `invoice.payment_failed` (create/refresh a `failed_payments` row, classify decline, schedule the next smart retry) and `invoice.paid` (mark recovered, attribute the recovery method, skip pending retries).

## Stripe App install (merchant linking)

`GET /connect/stripe/authorize?email=...&companyName=...` redirects to the Stripe App Marketplace OAuth 2.0 install page. The `state` parameter is a Data-Protection-sealed, 15-minute-lived token (nonce + email/company + issued-at).

`GET /connect/stripe/callback?code=...&state=...` validates `state`, exchanges `code` using the app owner's Stripe key, encrypts the access and refresh tokens, and upserts the `Merchant` row by Stripe account id. The current embedded app is in `stripe-dashboard-app/`; the old `stripe-app/` is retired.

## Billing and trial

Production billing is controlled by `Billing__Enabled` in the service environment. The first 30 days after each merchant's `CreatedAt` are free. A recovery's `RecoveredAt` determines whether it is free, even if the monthly billing run happens later. Trial recoveries are stamped with `TrialWaivedAtUtc` so they cannot enter a later invoice. A connected merchant can receive at most one new invoice per UTC run month; existing pending or failed invoices may still be resumed. Uninstalled merchants do not receive new invoices even though their encrypted token remains stored.

To give a specific account time to finish setup without a floor-only bill, configure `Billing__MinimumGracePeriods__0__StripeAccountId` and `Billing__MinimumGracePeriods__0__UntilUtc` (ISO 8601 UTC, exclusive). Increment the index for another account. During the grace period, the monthly $29 minimum is waived for that Stripe account; the standard percentage fee still applies to actual post-trial recoveries. After the date, the usual minimum applies on future billing runs. Remove both settings to end a grace period early. This setting never changes an invoice already reserved or sent, so set it before the first-of-month 06:00 UTC billing run. Verify the Render environment after deployment and check the next billing run's fee invoices before describing the waiver as active to a customer.

The admin page calculates trial state from signup time and shows fee invoices sent separately. An invoice being sent does not prove it was paid; payment status is not tracked here. See `docs/pricing/`, `docs/terms/`, and `docs/docs/attribution-and-billing/` for customer-facing policy.

## Tests

```
dotnet test
```
