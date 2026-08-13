# RecoverFlow Stripe App

Embedded Stripe App (Dashboard UI extension) for RecoverFlow. App id: `com.recoverflow.dashboard`. Manifest: `stripe-app.json`.

Do not confuse this with `../stripe-app/`, the retired Retry Waste Audit app. That folder is a historical record (see its BLOCKED.md) and must not be modified.

## Views

| Viewport | Component | Shows |
| --- | --- | --- |
| `stripe.dashboard.customer.detail` | `CustomerDetail` | Recovery cases for the customer, with expandable recovery email progress |
| `stripe.dashboard.invoice.detail` | `InvoiceDetail` | The recovery case for the invoice, with recovery email progress |
| `stripe.dashboard.drawer.default` | `AppOverview` | Merchant recovery stats, last 30 days and all time |
| `settings` | `AppSettings` | Connection status and links to RecoverFlow |

## Backend

All data comes from the RecoverFlow API route group `/app/v1` (base URL is the manifest constant `API_BASE`). Every endpoint is POST. Each request sends:

- header `Stripe-Signature` from `fetchStripeSignature()`
- JSON body exactly `{"user_id":"...","account_id":"..."}` in that key order, the same bytes the SDK signed

The backend verifies the signature against the app signing secret (`Stripe:AppSigningSecret`, an `absec_` value that exists only after the first `stripe apps upload`). Until that secret is configured, the API answers 503 `app_not_configured` and the views show a setup notice.

Error codes handled by the UI: `merchant_not_connected` (connect call to action), `app_not_configured` (setup notice), `case_not_found` (empty state on the invoice view), anything else (retry banner).

## Development

```
npm install
npm run typecheck
```

Live preview in your own Dashboard (requires the Stripe CLI apps plugin):

```
stripe apps start
```

Note that `stripe apps start` defaults to `--manifest stripe-app.json`; it does not pick up an alternate manifest unless you pass one explicitly.

The preview reaches the **production** API, because a local one cannot be reached at all. Stripe validates `connect-src` at manifest load and rejects any entry that is not HTTPS on a public-suffix domain:

```
found violations for connect-src "http://localhost:5157/app/": protocol has to be https,
publicsuffix: cannot derive eTLD+1 for domain "localhost"
```

So pointing the app at `localhost` is not a configuration problem to work around, it is refused outright. To develop against a local API you need an HTTPS tunnel on a real domain (cloudflared, ngrok) and must put that hostname in both `connect-src` and `API_BASE`. Without one, develop against production.

Building and uploading are done by the Stripe CLI (`stripe apps upload`). Do not run the upload until the publishing account question is settled; the first upload permanently fixes the app id and generates the signing secret.

## Constraints

- Only components from `@stripe/ui-extension-sdk/ui` render inside the Dashboard sandbox. No DOM access, no refs, no localStorage.
- `@stripe/ui-extension-sdk` is pinned to 8.10.0, the last 8.x release built against React 17.0.2. The 8.11.x releases moved to React 18.
- Network requests may only target URLs declared in `ui_extension.content_security_policy.connect-src`, and every such URL must be HTTPS on a public-suffix domain.
- `API_BASE` resolves from `context.environment.constants` first and only falls back to the `PRODUCTION_API_BASE` constant in `src/lib/api.ts`. Editing that constant has no effect while the manifest defines `API_BASE`.
- The `listing/` folder (marketplace listing copy) is owned by a separate workstream.
