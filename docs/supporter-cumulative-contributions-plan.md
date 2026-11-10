# Cumulative supporter contributions

Status: implemented locally; not deployed or enabled for live payments. Run the additive database migration and Stripe sandbox acceptance before enabling the service. No keys, live purchases, publication or deployment were performed.

USD contributions use integer cents and cumulative thresholds of 300 / 700 / 1500 ($3 / $7 / $15). Thus $5 gives Supporter, another $2 gives Supporter+, and another $8 gives Super Supporter. Payments from $0.50 to $1,000 are accepted; the app offers the difference to the next tier. Higher tiers retain lower cosmetics. Robux independently accumulates approved pass denominations across connected accounts; ownership proves the configured fixed credit, not historical spending.

## Identity and recovery

The desktop generates a cryptographically random 256-bit recovery code before checkout and protects it with DPAPI CurrentUser. The service stores only its SHA-256 pseudonymous supporter ID. Requests authenticate with the recovery code in an HTTPS POST body. The code is a bearer credential: never log request bodies, place it in URLs, or send it to telemetry. Anyone holding it can restore the same identity. There is no email lookup, Roblox identity link, reset or rotation flow. Save it using Support settings before paying. A receipt export restores cosmetics; the recovery code restores the cumulative identity and future contributions. Restoring another identity requires an explicit UI confirmation and refuses while a checkout is pending.

## Durable credit

Apply services/support/schema.sql, then services/support/cumulative.sql to the dedicated support D1 database before deploying the updated Worker. The second file is additive and idempotent on an existing v1 database. Wallets, server-bound checkout attempts, provider payment IDs and adjustments are durable; the existing scheduled expiry only removes v1 attempts and rate budgets. They never remove cumulative history.

The service creates a server-priced USD Checkout Session with a bounded amount and exact wallet binding. A verified Stripe notification must match the deployment mode, product, session, wallet, amount and currency. A unique payment-intent ID and unique attempt credit each payment once. SQLite triggers update the wallet total, earned tier and revision within the credit statement's transaction. Failed writes return an error for provider retry. Checkout creation uses a D1 batch for wallet/attempt insertion and Stripe idempotency keys for request recovery.

Refund notifications retain the largest cumulative amount_refunded per payment; duplicates and out-of-order older amounts cannot restore credit. A closed lost dispute deducts its verified disputed amount, capped by the remaining payment credit. Won/active disputes do not deduct credit. Adjustments can precede payment completion and still prevent credit. Payment records retain the original gross USD amount, before fees. Earned tiers are a permanent high-water mark even when the net total falls. A later top-up uses the current net total toward the next unearned tier.

## Signed receipts and UI

Version 2 receipts add supporterId, tier (-1 for no tier), totalCents, revision and currency: usd to the existing product/badge/receiptId/version/keyId claims. Receipt ID equals the supporter ID. ES256 P-256 signatures use 64-byte IEEE P1363. The desktop checks the signature, exact claim set, bounds, tier consistency and claim/recovery ownership before saving. Older revisions cannot replace a newer receipt for the same identity. Version 1 receipts remain base Supporter recognition with no inferred historical amount. Historical reconciliation remains manual and is not credited merely by importing a v1 receipt.

Native verified tier drives the sidebar and studio entitlement. A tier increase observed while the Support page is open reveals the highest newly earned tier once; same-tier refreshes and reopening the page stay quiet. Appearance and owner preview controls cannot grant entitlement. Existing offline receipts remain usable during outages. The app only contacts the wallet service on explicit refresh/restore or an in-progress checkout; it does not silently refresh on startup.

## Verification and rollout

Automated fixtures cover $5+$2+$8, sub-threshold credit, boundaries, skipped tiers, duplicate events, wrong amount/currency/wallet/session, interrupted writes, reordered refunds, lost/won disputes, deductions arriving before credit, cleanup retention, receipt signatures, native ownership, restart, recovery on another device and quiet UI refresh. Fixtures never make a real payment or launch Roblox.

Subscribe Stripe webhooks to checkout.session.completed, checkout.session.async_payment_succeeded, charge.refunded and charge.dispute.closed. Before release, apply both schemas, configure a test-mode service and matching public verification keys, then exercise success/cancel/decline, retries, refund and dispute flows in Stripe sandbox and verify backups and database retention. No automatic historical credit migration is supplied. A lost recovery code requires manual reconciliation through provider records.
