# Optional technical error reporting

Reporting is off by default. There is no persistent user/device/install identifier. It is independent of supporter recognition and Roblox accounts. The app remains fully usable when reporting is off, offline, unconfigured, or unavailable.

Fresh interactive setup offers an unchecked checkbox and the complete disclosure. A retained explicit preference is displayed on reinstall. Upgrades/repair preserve it. Consent is saved only after successful installation by the initiating normal-user setup window; silent/elevated setup and canceled installation cannot opt in. Portable users can opt in through Privacy settings. Settings can disable reporting, clear the local queue, or send the existing summary. `rmi telemetry status|on|off|clear|send` provides the same controls; `on` requires explicit confirmation or `--yes`, while `off` is immediate.

Desktop automatically uploads while open. Helper/browser launches only enqueue consented technical failures locally; helper-only users send explicitly with `rmi telemetry send`. There is no resident uploader and no reporting network call in the game-launch path. Cancellation/declined user actions are excluded. Reports do not contain authentication state or account-session secrets.

## What is sent and retained

Wire schema 1 contains a random per-batch retry identifier and at most 20 counters. Each counter contains its UTC date (no time of day), app/core and helper/core build versions, coarse Windows family, architecture, launch source, one fixed technical error code, an optional signed numeric HRESULT/Windows error code, and a count. The packaged core/helper versions match and are reported explicitly in both version fields. Unknown exception text is never accepted. Raw reports, logs, usernames, Roblox user IDs, credentials, cookies, tokens, local paths, game/place/server IDs, command lines, URLs, hardware IDs and arbitrary strings are excluded.

Per-user consent and counters are stored under `%LOCALAPPDATA%/RobloxLaunchHelper/telemetry`, shared by desktop and helper. They remain independent of appearance/layout resets and imports. Missing/unreadable/malformed consent means off. Enabling commits positive consent only after queue initialization succeeds. Cross-process storage locking and an uploader file lease prevent competing writes/sends. Opt-out changes the consent epoch, cancels a running uploader, and purges queued data. A request already received cannot be recalled.

Local counters expire after seven UTC calendar dates, are capped at 1,000 new errors per UTC day and 256 counter buckets, and the serialized store cannot exceed 256 KiB. Batches contain at most 1,000 error occurrences. Offline/network failures retain the same batch identifier for retry; newly collected errors remain separate. The desktop scheduling loop provides bounded backoff; manual sending is explicit. No historical diagnostic files are replayed when reporting is enabled.

The server transactionally deduplicates batch IDs for seven dates and aggregates fixed counters for 90 dates. It stores no raw bodies or application-level IP/user-agent logs. Cloudflare still processes connection information such as source IP and may maintain its own platform/security records; the privacy notice must disclose this. Anonymous aggregate counts already received cannot be deleted for one user because there is no stable user identity. Public submissions are untrusted and counts are approximate, never unique-user statistics.

## Owner setup and operations

`Build.ps1`, `src/desktop/Build.ps1`, and `Package.ps1` read the public receiving endpoint from `BuildConfig.psd1`: `https://rmi-optional-errors.kt-apps.workers.dev/v1/error-batches`. The owner deployed and enabled ingestion and confirmed synthetic acceptance, D1 storage, and retry deduplication. User reporting remains off by default; configuring a receiving address never opts anyone in. Previously distributed binaries are unchanged and need an updated build to use this endpoint.

The infrastructure source lives in `services/telemetry`. For a new deployment, start with ingestion disabled and complete the following owner setup:

1. Keep the account on **Workers Free**; do not upgrade to paid Workers or enable other paid products. Use the included workers.dev hostname, so a domain purchase is unnecessary. Verify account/billing status before deployment.
2. Install the official Wrangler CLI in the owner's tooling environment. Create a D1 database named `rmi-optional-errors`, replace the placeholder database ID in `wrangler.jsonc`, and apply the migrations. Keep secrets and account tokens out of the repository and desktop binaries.
3. Run local verification: `npm test` and `npm run check` in `services/telemetry`. Tests use Node 24's built-in SQLite and do not require a service account, network, or paid dependencies.
4. Deploy with invocation logs/observability disabled as provided. Verify that failed requests, payloads and headers are not added to console logs or external tracing. Set `INGESTION_ENABLED=true` only after an end-to-end synthetic error batch succeeds against staging.
5. Build normally to compile the endpoint in `BuildConfig.psd1` into the core assembly. Override it with `./Package.ps1 -TelemetryEndpoint 'https://YOUR-WORKER.workers.dev/v1/error-batches'` (or the same flag on `Build.ps1`). Pass `-TelemetryEndpoint ''` explicitly for an unconfigured build. Direct MSBuild users must pass `-p:TelemetryEndpoint=...`; the PowerShell settings file is not automatically loaded by MSBuild.
6. Confirm default-off behavior in the packaged installer/portable app, then enable with synthetic data and inspect the resulting aggregate. Test opt-out during an in-flight request. No Roblox credentials or live game launches are necessary for this check.

The route-level rate limiter uses a common route key rather than user/IP identifiers. Its limit is approximate per Cloudflare location. A transactional database budget limits accepted batches to 1,000 per UTC day globally; it does not stop incoming requests or database reads from consuming provider quotas. The fixed payload limit is 8 KiB. Worker/database failures return retryable errors, and the app remains functional.

Workers Free currently stops at 100,000 account requests/day; D1 Free stops queries after its daily read/write allowances and permits 500 MB per database. Provider limits and quotas can change: verify [Workers limits](https://developers.cloudflare.com/workers/platform/limits/), [D1 pricing](https://developers.cloudflare.com/d1/platform/pricing/), and [D1 limits](https://developers.cloudflare.com/d1/platform/limits/) before deployment. Approximate [rate-limit bindings](https://developers.cloudflare.com/workers/runtime-apis/bindings/rate-limit/) are additional protection, not a strict billing cap. On a Free account, exhaustion causes outages instead of paid overages. Keep services fail closed; do not automatically purchase a paid tier.

Inspect aggregate trends with D1's owner-only dashboard, for example:

```sql
SELECT app_version, helper_version, code, SUM(count) AS errors
FROM telemetry_errors GROUP BY app_version, helper_version, code
ORDER BY errors DESC LIMIT 30;
```

The daily cron removes expired aggregates/receipts. Check its success and D1/Workers quota usage weekly and after releases. Disable ingestion with `INGESTION_ENABLED=false` during an incident. D1's free point-in-time recovery can retain data for seven days beyond table deletion; include provider retention in the published disclosure. Do not add identifiers to diagnose abuse. Separate supporter and telemetry databases/routes retain data separation, but same-account Workers/D1 quotas are shared; telemetry floods can interrupt supporter service. A payment webhook must return non-2xx if its durable receipt write fails so the payment provider retries.

## Download counts without app tracking

Run `scripts/Get-DownloadCounts.ps1` from an owner shell. It reads paginated GitHub release assets using the [release REST API](https://docs.github.com/en/rest/releases/releases), and counts only the exact desktop installer, helper installer and portable ZIP names. Checksums, manifests, source archives and drafts are excluded; published prereleases remain clearly marked. Asset IDs are deduplicated if releases change during pagination.

For a private repository set `GH_TOKEN` or `GITHUB_TOKEN` in the owner's environment with repository read permission. Never put the token into the app. Optional `-SnapshotPath` appends JSON Lines to an existing directory so removed/replaced assets do not erase older observations. Counts represent download requests, including repeat downloads and updates; they do not measure unique people, installs, or active users. `scripts/Test-DownloadCounts.ps1` verifies the allowlist, deduplication and append behavior using local synthetic releases.


The README Downloads badge uses the same runnable-package allowlist, including published prereleases. The `Publish download badge` workflow paginates GitHub releases and assets, then deploys only generated badge artifacts to GitHub Pages hourly and after release changes. It never commits counter updates or modifies the README. Failed API requests leave the last successful deployment intact. Scheduled runs and GitHub image caching can delay updates. The exact total is also available at https://lanceperdue23.github.io/roblox-multi-instance/downloads.json.
