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

## Stripe Connect (merchant linking)

`GET /connect/stripe/authorize?email=...&companyName=...` redirects to Stripe's Standard OAuth authorize page. The `state` parameter is a Data-Protection-sealed, 15-minute-lived token (nonce + email/company + issued-at) — CSRF-safe per Stripe's OAuth guidance, no server-side session storage needed.

`GET /connect/stripe/callback?code=...&state=...` validates and unseals `state`, exchanges `code` for the merchant's access token via the platform secret key (`OAuthTokenService`), encrypts the access token (AES-256-GCM, `Encryption:Key`), and upserts the `Merchant` row by Stripe account id. RecoverFlow only reads/acts on data the merchant already owns via Standard Connect — it never creates or configures connected accounts, so Connect's Accounts v2 API doesn't apply here.

## Tests

```
dotnet test
```
