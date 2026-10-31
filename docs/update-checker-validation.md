> Historical feature validation notes. Current desktop testing uses root Test.ps1 and Audit.ps1; shared/helper checks use Test-Shared.ps1 (including -Integration when explicitly required). Retired UI instructions below are historical. See [milestone audit](milestone-audit.md).

# Update checker release and acceptance checklist

## Current distribution and publication

Use the shared version in `Directory.Build.props`, root `Package.ps1`, and `Test-Distribution.ps1`. The release assets are edition-specific installers plus the portable ZIP, checksums and `assets.json`; a standalone helper executable is no longer the current distribution. Follow [distribution preparation](distribution-preparation.md) and the [v1.0.0 acceptance gate](releases/v1.0.0.md). Keep the repository private; both editions are validation prereleases pending live acceptance. The unauthenticated updater cannot access private releases and deliberately ignores prereleases; authenticated manual downloads remain necessary.

## Historical standalone-helper publication procedure (superseded)

1. Set `Version` in `src/RobloxLaunchHelper.csproj` to the release version and update the changelog. Use a stable numeric version such as `1.2.0`.
2. Run `Test.ps1` and `Build.ps1` with the supported .NET 10 SDK. Publish the resulting self-contained Windows x64 `RobloxLaunchHelper.exe`; an ordinary framework-dependent build is not the release payload.
3. Create a public GitHub Release in `LancePerdue23/roblox-multi-instance` with a matching tag, such as `v1.2.0`, and meaningful release notes. Mark the intended stable release as Latest. Drafts and prereleases are excluded.
4. Attach exactly one asset named `RobloxLaunchHelper.exe`. After upload completes, verify the release API includes its expected byte size, repository download URL, and a `sha256:` digest matching the SHA-256 emitted by `Build.ps1`.
5. Publish only when the asset and notes are ready. Do not replace an asset while users are downloading it; publish a new version for changed binaries. Missing digests or ambiguous assets disable in-app download and explain why.

No website, GitHub token, scheduled updater, or startup service is needed. GitHub hosts the executable and metadata. The API digest checks the downloaded bytes against GitHub's metadata; it is not a publisher code-signing certificate. Existing unsigned-build Windows prompts still apply.

## Automatic and manual checks

- The first normal app/Roblox launch in a Windows boot claims a per-account attempt. Diagnostics and desktop/browser launches share that claim. A temporary normal-user update process performs the HTTP request; Roblox launch does not wait for networking.
- Claiming uses the native Windows boot identifier, a per-account named mutex, and atomic `updates/boot.txt` state under `%LOCALAPPDATA%/RobloxLaunchHelper`. Sleep and app restarts do not start a new boot. Failure to read the boot identifier or use state storage skips automatic checking.
- Offline, failed, malformed, rate-limited, and timed-out automatic requests remain silent and consume the attempt. **Updates…** in Diagnostics allows manual checking regardless of the boot claim. An already-open update helper handles manual requests instead of starting a second check.
- A newer stable release produces a compact nonmodal card without activating it or playing sound. **Later** and close dismiss it for this boot. Optional launch-notification preferences do not control update notices.
- Versions compare numerically with an optional lowercase `v` prefix and two to four components; omitted components normalize to zero. The installed file version takes precedence over a newer downloaded executable's version. An unreadable installed version is not guessed.

## Download and installation acceptance

Use a disposable Windows account with an older installed helper and a compatible newer GitHub release. Synthetic tests do not establish live GitHub availability or successful elevated replacement.

1. Launch 20 clients in the same boot. Confirm one automatic metadata request/card, no focus change, and normal Roblox startup. Close/reopen Diagnostics and confirm no second automatic notice.
2. Repeat offline on the first launch of a boot: no warning popup or repeated automatic request. Reconnect and use **Updates…** to check manually. Restart Windows and verify automatic eligibility resets.
3. Inspect the card at normal and 150% display scaling, including keyboard navigation. Open **What's new**, verify notes remain plain text, and close the card with its notes window open. The optional release-page link must open only this repository's HTTPS release page.
4. Select **Download update**. Verify percentage progress, cancel, retry, and normal handling of a disconnected network or insufficient disk space. No installer or browser opens automatically.
5. Confirm **Run setup** appears only after size, SHA-256, and embedded executable version verification. Mismatched or incomplete downloads must never run.
6. Select **Run setup** with Diagnostics, Active instances, and tray mode open. Confirm graceful closure and Windows administrator approval. Keep Roblox games open. A worker, another session's helper, or another file user must cause a bounded wait and retry message rather than termination or an attempted overwrite while known busy.
7. Cancel elevation and retry from the retained setup window. Confirm cancellation is distinguished from setup failure. Complete setup, verify the installed version, then use **Open app** to open Diagnostics.
8. Confirm browser-integration preference, launch-notification settings, layout preferences, and existing installation settings survive. Follow the normal installer/browser acceptance checks in `CONTRIBUTING.md`.
9. Verify update processes exit after their windows close. Unused known staging directories older than seven days are pruned during subsequent update activity; recent, leased, or unexpected content is retained.

Internal `--check-updates`, `--updates`, and `--update-handoff` modes are implementation details, not new supported command-line interfaces. Setup handoff validates staging layout and parent identity, waits for the source process to exit, rechecks the payload, signals same-session installed managers to close, and uses Windows Restart Manager only to inspect file users. It never calls a process shutdown or force-kill API.

Fully unattended downloading/installation, transactional rollback, and changing the main EXE's default launch behavior remain separate work.
