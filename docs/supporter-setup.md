# Optional contributions: Stripe and Roblox gamepass

Stripe Checkout, verified payment webhooks, and offline supporter receipts are implemented locally. Robux donations open an owner-configured Roblox gamepass in the external browser. These are independent options: either can be enabled while the other remains unavailable. Neither changes access to app features.

## Approved tier prices

One-time USD contributions: Supporter **$3**, Supporter+ **$7**, Super Supporter **$15**. Cosmetics are permanent, with higher tiers including lower-tier cosmetics. These are the approved display prices; Stripe Price IDs and tier-aware checkout/receipt issuance still need configuration and implementation before selling the tiers. Existing single-price checkout must not be presented as selecting among these tiers. The matching Robux contributions are 300, 700, and 1,500 Robux respectively. Gamepass IDs remain blank.

## Roblox group gamepass setup

The gamepass does not need to exist to build or test the app. Until it is created, leave `robloxGamePassId` null or omit it from `support-config.json`; the Support page shows a disabled **Support with Robux…** button and a coming-soon message.

When ready, create the donation pass for a published experience owned by the intended group, enable it for sale, choose its Robux price, and verify the displayed creator/group and sale availability on Roblox. See [Roblox passes documentation](https://create.roblox.com/docs/production/monetization/passes). The app only opens the pass page; Roblox handles the purchase and group proceeds. No group credentials or group ID belong in the app.

Set the pass ID as a **string** in the public configuration. A Robux-only configuration needs no Stripe endpoint or signing keys:

```json
{
  "endpoint": "",
  "publicKeys": {},
  "robloxGamePassId": "123456789"
}
```

The number above is an example, not a donation destination. Replace it with your actual pass ID before enabling or distributing. To enable both methods, add `robloxGamePassId` to the generated Stripe public configuration and retain its real endpoint and public keys. Bundle with `./Build.ps1 -SupportConfig 'path/to/support-config.json'` or `Package.ps1 -SupportConfig`.

Only a canonical positive numeric ID is accepted. The backend constructs `https://www.roblox.com/game-pass/ID`; browser-provided URLs and saved Roblox sessions are not used. A pass is a one-time purchase per Roblox account. The app checks ownership of configured passes on connected accounts through Roblox’s inventory is-owned endpoint. A positive response grants local app-wide cosmetic supporter recognition, retained offline and after account removal; this is separate from exportable Stripe receipts. Private inventories, API failures and rate limits leave verification unresolved and never revoke cached recognition. Opening the page never marks a payment complete. Before release, manually check that the configured link opens the correct group's pass and Roblox shows the intended sale price.

For multiple donation passes, add a string array `robloxGamePassIds`; any configured pass qualifies, with at most 16 distinct pass IDs checked. The single `robloxGamePassId` remains supported and is the donation link when provided. Leave both fields blank/omitted until real passes exist. No placeholder pass IDs are bundled by development builds.

Checks run when support status is requested at startup (throttled to six hours within a running app), after connecting an account, and via **Check purchases**. Only locally saved connected user IDs are used; bridge payloads cannot choose arbitrary user IDs or pass IDs. Requests go directly to Roblox without cookies. A protected local recognition marker stores no account IDs. The first successful match qualifies the installation. Hide recognition in Support settings using the existing badge visibility control. To transfer Robux recognition to another computer, connect the owning account and check there; Stripe receipt import/export remains independent.

## Stripe supporter receipts

All app features are free. A completed one-time Stripe contribution issues a cosmetic local supporter receipt. There are no app accounts, Roblox identity links, subscription checks, or feature gates. The unconfigured build cannot start payments and sends no support requests. Nothing here has been deployed or connected to a real payment account.

The desktop opens Stripe-hosted Checkout in the external browser. A separate support Worker receives payment notifications and keeps only opaque checkout IDs/URLs, random attempt IDs, claim-secret hashes, payment state, and expiry dates in its own D1 database. It does not persist donor names, emails, addresses, webhook bodies, or Roblox identifiers. Stripe necessarily processes and retains payment information, visible to the payment account owner through Stripe. Cloudflare handles network metadata; this is not a claim that payment infrastructure is anonymous.

## Local verification

Run from the active checkout:

```powershell
node --test services/support/test/worker.test.mjs
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.build/home'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.build/packages'
.\.build\sdk\dotnet.exe run --project src/desktop/support-tests/Support.Tests.csproj -c Release -m:1 -p:BuildInParallel=false -p:NuGetAudit=false -p:UseSharedCompilation=false --disable-build-servers
```

These fixtures use generated keys and mocked payment responses; they do not make purchases, deploy infrastructure, or require an account. A real Stripe sandbox and Cloudflare deployment remain owner acceptance work.

## Owner prerequisites and configuration

1. Create a Stripe account, complete its requested identity/business and payout onboarding, and create a **one-time** Price for optional support. Choose the amount yourself; there is no hardcoded amount in the app or Worker. Do not configure a recurring Price. Start with Stripe test mode. Hosted card checkout is the only enabled payment method in v1.
2. Create a Cloudflare account on Workers Free. Use a dedicated support Worker and **separate D1 database** from diagnostics/telemetry. From `services/support`, install/use the official Wrangler CLI, authenticate, run `npx wrangler d1 create rmi-support`, and add the returned real database ID/name to `wrangler.jsonc` under `d1_databases` with binding `SUPPORT_DB`. Then run `npx wrangler d1 execute rmi-support --remote --file schema.sql`. Do not publish an app configured for an untested service.
3. Generate an ES256 key pair from the repo root:

```powershell
node services/support/tools/generate-keys.mjs .build/support-keys production-1
```

Back up `.build/support-keys/receipt-private.jwk` outside the checkout in secure owner storage. It must never be committed, included in an app package, pasted into diagnostics, or printed in logs. The generated `support-config.json` contains only public key material. Changing keys requires a new key ID; preserve old public keys in future desktop builds so existing receipts keep working.

4. Configure these Worker secret bindings using `npx wrangler secret put NAME` from `services/support`: `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, `STRIPE_PRICE_ID`, `STRIPE_LIVE_MODE` (`false` for sandbox), `RECEIPT_KEY_ID` (the exact generated ID), and `RECEIPT_PRIVATE_JWK` (the private JWK file contents). Owner-only shell input or a secret manager should supply them. Set up a Stripe webhook pointing to the deployed HTTPS origin's `/support/webhook`, subscribing only to `checkout.session.completed` and `checkout.session.async_payment_succeeded`. Obtain the endpoint's signing secret from Stripe and update the binding. Deploy with `npx wrangler deploy` only when intentionally ready. Observability is disabled; never enable logging of request headers/bodies or payment payloads.
5. After sandbox acceptance, configure corresponding **live-mode** Stripe keys, Price, and webhook secret in an independent production deployment and set `STRIPE_LIVE_MODE=true`. Verify live/test separation. Use the actual deployed HTTPS origin ending in `/` as the public `endpoint` in the generated `support-config.json`. Bundle that public file beside the desktop executable through the release's reviewed build/packaging process. A missing or invalid configuration leaves support disabled. Do not include private JWKs or Stripe keys in desktop resources.

For the D1 binding, add an actual object with `binding`, `database_name`, and `database_id` fields; the checked-in deployment file intentionally has no fake ID. A custom domain is optional; Cloudflare's HTTPS `workers.dev` origin works. The checked-in `SUPPORT_RATE` binding applies approximate route-wide limits of 60 requests/minute per Cloudflare location to start/claim, using no IP or visitor identifiers. Its namespace ID must be unique in your Cloudflare account; change it if already allocated. Stripe webhooks bypass this limiter. An atomic D1 budget permits at most 100 new checkout attempts/day globally; repeat starts using the same claim hash reuse one attempt and do not spend budget. These conservative defaults protect a small free deployment but can deny legitimate requests during abuse; review them against real usage before increasing. Monitor free-tier request/CPU/D1 usage through provider dashboards without logging payloads. Edge protection can reduce abuse before Worker execution. The app never uses a stable telemetry ID as a payment identity.

Use `./Build.ps1 -SupportConfig '.build/support-keys/support-config.json'` or the same `-SupportConfig` flag on `Package.ps1` to bundle the public configuration. Without this flag, builds leave payments unconfigured and remove stale configuration from reused desktop output folders. The file is public endpoint/key material only; it must never contain a private signing key or Stripe credentials.

## Protocol and retention

`POST /support/start` accepts only a SHA-256 claim-secret hash (`claimHash`, lowercase hex). It creates a random attempt, then a Stripe Checkout Session bound to that attempt using server-owned metadata and the configured Price. A unique claim hash and Stripe idempotency key let an interrupted request resume the same checkout rather than creating another. The desktop protects the claim secret with Windows DPAPI CurrentUser **before** sending the first request, then retains it if that request fails. The response contains `attemptId` and `checkoutUrl`. The desktop validates the HTTPS Stripe host before opening it. Unknown request fields are rejected. API keys remain on the Worker.

`POST /support/claim` accepts `attemptId` and the secret in a POST body. An authenticated pending claim returns `status: pending`. After a verified paid notification, it returns `status: paid` and a signed receipt. The raw-body webhook HMAC and timestamp must verify; product marker, mode, live/test flag, positive paid amount, random attempt ID, and exact server-created session ID must match. Updating a session to paid is durable and idempotent. Webhook errors do not acknowledge success; Stripe can retry. A browser redirect never grants supporter status.

Checkout expires after 24 hours. Pending claim credentials last 30 days, allowing service/webhook delays and app restarts. Starting support again resumes the pending checkout without replacing its claim secret. Once checkout expires, check payment before using the explicitly confirmed Forget pending checkout action to begin again; forgetting deletes local claim credentials, so a payment already completed may require manual receipt recovery. It does not cancel/refund a provider payment. Daily cleanup removes expired support rows; this includes issued payment references. Local receipts do not expire, and can be restored without the service. Lost receipts after retention may require manual reconciliation using the owner's Stripe records; there is no automatic email/account lookup. The app only polls while the user is in the Support page or explicitly checks payment; offline badge display makes no Stripe requests. Configured Roblox ownership checks at startup are separate from Stripe.

The receipt is `base64url(JSON payload).base64url(ES256 signature)`. Sign the payload's UTF-8 bytes using P-256/SHA-256 and a 64-byte IEEE P1363 signature, not ASN.1 DER. Its only claims are `product: roblox-multi-instance`, `badge: supporter`, a random `receiptId`, `version: 1`, and the owner-selected `keyId`. The desktop verifies with bundled base64-encoded DER/SPKI public keys before import or activation. Receipt files use `.rmisupport`. Local storage is DPAPI protected; explicit export produces a portable bearer receipt. Receipts are copyable and cannot establish the identity of a particular person. Hide/show does not erase supporter status; Remove deletes only the local receipt; forgetting a pending checkout is a separate confirmed action. Refunds do not revoke an already issued offline cosmetic receipt. No features depend on receipt state.

## Acceptance before enabling in a release

Use Stripe sandbox to verify success, cancellation, declined payment, repeated notification, delayed notification, restart during payment, wrong live/test keys, tampered webhook, and wrong session/product. Confirm that badge activation occurs only after the server confirms payment, all ordinary features still work without support, no payment/body data appears in logs, local offline verification works, and export/import survives a new Windows account/device. Verify packaged builds include only the public configuration. Measure Worker CPU before relying on its free plan; quota exhaustion must leave existing offline badges and all app features working. No live purchase is needed for automated tests.

Stripe standard US pricing currently lists no monthly/setup fees and 2.9% + $0.30 per domestic-card payment; country/method fees vary. Workers Free and D1 have finite quotas and can support a small service at no hosting cost; stay on the free plan if avoiding recurring charges is required. A payment account and payout onboarding are still required. See [Stripe pricing](https://stripe.com/pricing), [hosted Checkout](https://docs.stripe.com/checkout/quickstart), [webhook security](https://docs.stripe.com/webhooks), [Workers pricing](https://developers.cloudflare.com/workers/platform/pricing/), [D1 pricing](https://developers.cloudflare.com/d1/platform/pricing/), and [Cloudflare secret bindings](https://developers.cloudflare.com/workers/configuration/secrets/).
