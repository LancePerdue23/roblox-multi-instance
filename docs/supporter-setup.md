# Optional contributions: Stripe and Roblox gamepass

Stripe Checkout, verified payment webhooks, and offline supporter receipts are implemented locally. Robux support opens an approved group pass in the external browser. These are independent options: either can be enabled while the other remains unavailable. Neither changes access to app features.

## Approved tier prices

One-time USD contributions: Supporter **$3**, Supporter+ **$7**, Super Supporter **$15**. Cosmetics are permanent, with higher tiers including lower-tier cosmetics. These are the approved display prices; Cumulative USD checkout and tier-aware receipts are implemented locally; service deployment and sandbox acceptance remain required. The matching Robux contributions are 300, 700, and 1,500 Robux respectively. All 21 approved Robux passes are bundled by default.

## Roblox group passes and cumulative credit

Normal builds bundle the approved donation catalog for MultiInstance group **656550689**, Donations place **132006559657920**, universe **10775556311**. The 21 pass IDs and fixed denominations are in src/desktop/backend/RobuxSupport.cs, verified against Roblox's public catalog on November 9, 2026. The default configuration has an empty Stripe endpoint and no signing keys; Robux needs no service deployment or credentials.

Robux tiers use cumulative fixed credit: **300 / 700 / 1,500** for Supporter / Supporter+ / Super Supporter. A 700 pass and 500+200 both qualify for Supporter+, including when the passes belong to different connected accounts. One account/pass pair counts once; the same pass bought on two accounts counts twice. Credit represents the approved denomination, not the historical price paid, regional pricing, or group proceeds. New passes require an explicit catalog update; later sale-price changes do not alter saved credit.

Purchase verification is desktop-only, using native connected accounts and Roblox's public endpoints without cookies or hosted services. The single Verify purchases action automatically discovers all approved support passes, independently of the amount chosen for buying. It starts with `apis.roblox.com/game-passes/v1/users/{userId}/game-passes?count=100&exclusiveStartId=`, matching owned IDs to fixed catalog credit. An accessible inventory of fewer than 100 passes can resolve in one request per account. Listing prices, sale status, and unrelated pass IDs never determine credit.

Listings traverse at most three pages of 100 passes, using the last returned pass ID as the next cursor. Only a fully validated short page establishes non-ownership of unseen passes. Full pages, repeated IDs, malformed/oversized responses, and failed requests cannot establish absence. Unresolved donation passes fall back to individual `inventory.roblox.com` is-owned checks. Already verified purchases require no request; when fewer than two pairs remain unresolved, the app goes directly to individual checks. Thus even inventories containing thousands of unrelated passes have bounded traversal. Listings are bounded to 1 MB per page, and credits from each valid page are saved before continuing.

Support status reads, startup, and connecting an account display saved recognition immediately and never start inventory scans. All scans have at most four requests in flight, ten-second request deadlines, and a 90-second scan deadline. Request starts are spaced by at least 100 milliseconds. Rate-limited (429) and temporarily unavailable (503) requests share a Retry-After cooldown, including when switching from listing to direct fallback. Direct requests retry automatically up to five times per unresolved pair. Throttling doubles request spacing up to two seconds; successful direct responses gradually restore normal pacing. Progress and partial results remain explicit. The six-hour completed-scan throttle and 15-second incomplete-scan cooldown include account set and scope; manual verification requests a fresh scan. No scan can remove cached credit.

Interrupted discovery checkpoints successful non-ownership results in the protected ledger for 15 minutes, so retrying after a failure or restart skips completed pairs as well as permanently verified purchases. A completed scan clears its negative checkpoints so a fresh scan can detect new purchases. The native selected-pass compatibility path remains available and bypasses negative checkpoints, while the page always uses automatic discovery. The full catalog sums to 198,000 fixed Robux credit for an account owning every approved pass. Real completion times depend on Roblox's response times and throttling; the scan deadline reports partial progress instead of claiming completion.

A version 2 DPAPI CurrentUser ledger stores an installation salt and hashed account/pass pairs with fixed credit. It stores no raw account IDs. It is local to this Windows profile and installation; connect the same accounts and verify the relevant passes, or use full discovery, on another computer to restore recognition. The old protected base-supporter marker is retained with zero inferred credit. USD and Robux totals are separate; the highest earned tier determines cosmetics.

The Support page puts Robux first. Its slider snaps to the 21 approved pass amounts, with a synchronized exact selector. Milestones shift by verified credit: at 500, 200 reaches Supporter+ and 1,000 reaches Super Supporter. Buy pass opens the selected allowlisted Roblox URL; the browser's signed-in account determines who purchases it. Connect that account here and choose Verify purchases. Opening a pass or previewing a projected tier never grants credit.

An explicit public support-config.json still overrides the bundled defaults. Existing robloxGamePassId / robloxGamePassIds configurations remain supported as base recognition with no inferred amount. To configure fixed-credit passes explicitly, use robloxPasses entries with string id and integer credit, alongside endpoint and publicKeys. Build.ps1 -SupportConfig and Package.ps1 -SupportConfig remain available; no private Roblox credentials belong in this configuration.

## Stripe cumulative contributions

See [cumulative implementation and protocol](supporter-cumulative-contributions-plan.md) for identity, receipts, retention and refund behavior. USD contributions accumulate for one private recovery code across checkouts and devices. No app features require support. Robux passes separately accumulate fixed credit at the matching thresholds; totals are never converted between currencies.

### Owner setup (not performed automatically)

1. Use a Stripe test account and a separate Cloudflare Worker/D1 database from telemetry. Keep observability off and never log request bodies, recovery codes or payment payloads.
2. Create the real D1 binding SUPPORT_DB in services/support/wrangler.jsonc. Run schema.sql and then cumulative.sql against that database. Both files are safe to rerun; cumulative history is durable and must be backed up. The old cleanup job only removes v1 attempts and rate budgets.
3. Generate ES256 keys with node services/support/tools/generate-keys.mjs .build/support-keys production-1. Back up the private JWK securely outside the repository; never commit, package or print it. Preserve old public verification keys when rotating signing keys.
4. Configure Worker secrets STRIPE_SECRET_KEY, STRIPE_WEBHOOK_SECRET, STRIPE_LIVE_MODE (false for sandbox), RECEIPT_KEY_ID and RECEIPT_PRIVATE_JWK. New cumulative checkout uses validated inline USD pricing, so STRIPE_PRICE_ID is needed only if continuing to support old v1 clients. It must be a one-time Price. Subscribe the webhook endpoint /support/webhook to checkout.session.completed, checkout.session.async_payment_succeeded, charge.refunded and charge.dispute.closed. Use the endpoint's Stripe signing secret.
5. Deploy only when explicitly authorized. Use the real HTTPS origin ending in / in the generated public support-config.json and bundle with Build.ps1 -SupportConfig or Package.ps1 -SupportConfig. The public file contains endpoint and public keys only. Missing configuration leaves payments disabled. No deployment or real account setup has been performed here.
6. Validate in Stripe sandbox before configuring an independent live deployment. Verify mode separation, checkout amounts and identity, delayed/duplicate webhooks, restarts, cancellation/declines, refunds, won/lost disputes, recovery on another device, offline use, package contents and all free functionality. No live purchase is required for automated fixtures.

### Protocol

POST /support/start-v2 accepts claimHash (64 lowercase hexadecimal characters), walletSecret (43 base64url characters) and amountCents (integer 50–100000). Server binding fixes wallet and amount for retries. The app persists the recovery and per-attempt claim secrets before sending checkout requests. Checkout expires after 24 hours; pending claim authentication lasts 30 days. The wallet recovery code can restore credited contributions after this period.

POST /support/claim accepts attemptId and secret and returns pending or a signed cumulative receipt after durable provider credit. POST /support/wallet accepts walletSecret and returns the current signed receipt, including a tier of -1 below $3. Older POST /support/start and v1 claims remain compatible when the legacy Price is configured. Browser thanks/cancel pages never grant recognition.

The service stores opaque checkout and payment-intent IDs, amounts, USD currency, hashed supporter identity, refund/dispute deductions and revisions. It does not store donor names, email addresses, raw webhook bodies, recovery codes, Roblox IDs or telemetry identities. Stripe processes payment details and retains its financial records. Cloudflare handles network metadata.

Receipts use .rmisupport and are portable signed cosmetic bearer artifacts. The distinct private recovery code is needed to accumulate later contributions under the same identity; importing a receipt does not supply that credential. DPAPI protects local secrets and receipts. The UI can reveal/hide the recovery code only on explicit action. A wrong or stale wallet receipt cannot replace current recognition. Refunds reduce the credited total while already earned cosmetics remain permanent. Closed lost disputes deduct their verified disputed amount, won disputes do not. USD and Robux are never converted or added together.

Rate controls remain approximate route-wide limits plus 100 newly created checkouts/day, without IP identifiers. Budget rejection cannot remove existing recognition. Review hosting/provider quotas before rollout. Legacy v1 receipts contain no amount and cannot establish historical cumulative credit without provider reconciliation.

### Local fixtures

Run node --test services/support/test/worker.test.mjs and the native project src/desktop/support-tests/Support.Tests.csproj using .build/sdk/dotnet.exe with the repository's local SDK/package environment. Root Test.ps1 runs the desktop, web and feature checks. Fixtures use generated keys and synthetic payment responses; they do not deploy, purchase or launch Roblox.

Implementation references: [Stripe Checkout creation](https://docs.stripe.com/api/checkout/sessions/create), [Stripe webhook security](https://docs.stripe.com/webhooks), and [D1 transactional batches](https://developers.cloudflare.com/d1/worker-api/d1-database/#batch).
