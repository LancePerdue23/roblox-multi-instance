# Public-release readiness review — November 9, 2026

## v1.2.0 follow-up

The owner requests all current features in one unsigned validation prerelease, with App + Helper, Helper only and portable downloads. Development-build testing was reported successful; no VM is available, and packaged installation/update/uninstall acceptance is explicitly pending. Building, inspecting and downloading packages does not install them or overwrite the development output. Packaged and development apps under the same Windows account can share settings/accounts and helper integration; portable is not an isolated profile.

The release includes the configured opt-in Cloudflare telemetry endpoint, new account/shortcut/supporter work, public stable updater behavior, license and Windows CI. Supporter hearts ship only Ember, Orbit and Radiant; the previous Classic implementation is archived under docs/history and excluded from the built JavaScript, CSS and packages. Saved motion and verified tiers are preserved. Robux recognition is bundled. Stripe source/client functionality is included but payments remain unavailable without separate service configuration and sandbox acceptance. The local supporter role switcher remains development-only.

For this candidate, the refreshed prepublication remote inventory contains ten historical prereleases and 50 assets; all match the cached metadata hashes. Source/history scanning and all historical installer/ZIP inspection found no matched credential patterns or private-file paths (5,956 archive members). New candidate inspection covered 1,195 archive members with no matched credential patterns or private-file paths. Local desktop/frontend/account/support/service checks and all 147 shared checks passed; both installers and the portable ZIP passed distribution verification. Badge regression tests and actionlint passed. Recheck rebuilt artifacts and the hosted Windows CI result before upload. Do not describe the candidate as signed or as having passed the packaged Windows matrix.

Repository visibility remains private. Publication of the validation release does not authorize changing visibility; private vulnerability reporting and anonymous downloads must still be verified after a separately authorized public transition. The sections below retain the initial review's historical inventory and findings.

## Initial review

The repository is still private. Ten published releases (v1.0.0 through v1.1.2,
with separate historical helper tags) are all prereleases; there is no stable
release. This review does not change visibility, promote releases, replace
assets, or publish the working tree.

## Badges and download links

The existing `.github/workflows/download-badge.yml` already handles GitHub's
`public` event. It now prepares both Release and Downloads badges, also handling
published/edited/deleted releases and manual dispatch. While private, it writes
local SVG snapshots. Once public, it switches README images to live Shields.io
URLs and leaves snapshots alone. There is no scheduled counter workflow.
Actions is enabled remotely; the default token is read-only, and this badge
workflow explicitly requests contents write. A branch rule that blocks bot
pushes requires applying the generated README changes through the normal PR
process. Manual dispatch can retry a missed public event.

Release selects stable `vX.Y.Z` desktop/shared releases by semantic version,
excluding prereleases and historical `helper-` tags. The private snapshot says
`no stable release` until one exists; the public service can display unavailable
when no stable release exists. Downloads counts **all uploaded assets** in
published releases, including prereleases, checksums and manifests. It excludes
drafts and generated source archives. Repeats, updater requests and this audit's
downloads count; removed/replaced assets lose their old counts. This is not
unique users, installations, or the runnable-package-only metric available via
`scripts/Get-DownloadCounts.ps1`.

README installer links were checked against the published asset inventory and
retain v1.1.2 / helper-v1.1.2 while these are the available validation packages.
The portable ZIP is in v1.1.2. A `releases/latest/download/...` link would currently
fail because there is no stable release. After stable publication, update the
desktop link to the new release; retain the applicable helper link if an app-only
release does not include a new helper installer. Install one edition, upgrading
in place; uninstall first when changing editions. Portable launch/maintenance
still requires explicit helper setup.

Current source offers anonymous public stable updates, with explicit verified
downloads and setup handoff. Existing published binaries predate the current
updater changes; documentation does not update those binaries. Validation
prereleases require manual download. No token belongs in the app.

## History and published-asset inspection

Remote branches/tags were fetched before concluding the review. All 35 commits
reachable through local/remote refs were included; their 666 unique Git blobs
were scanned along with the nonignored working files present at audit time.
Historical paths were checked for private keys, local assistant/owner files,
account-bound badge deliveries, runtime profiles, logs, diagnostics and local
configuration. No matching private-file path was found. Credential-pattern
checks covered GitHub tokens, AWS access keys, Stripe live secret keys, private
key PEM headers, Roblox cookie warnings and JWTs; no match was found.

All **50 uploaded assets** from all ten releases were downloaded under ignored
`.build/public-release-audit/`. Sizes and SHA-256 digests matched GitHub's asset
metadata. The five portable ZIPs and all ten installers' embedded payload ZIPs
were inspected without running installer code. This covered **5,956 archive
members**, plus unpacked installer bundle entries. No matched credential
patterns, private-file names, debug symbols, local supporter switches, signed
private badge deliveries or machine configuration were found. Manifests and
checksums were part of the inventory. Local reports are `report.json` and
`embedded-report.json` in that audit directory.

This is a bounded review, not proof that no secret exists: arbitrary credential
formats, encrypted content, unreachable/deleted Git objects, provider-retained
objects, previously replaced release assets and external service state are not
covered. Recheck any new commits or assets before changing visibility. If a
secret is identified, revoke/rotate it before deciding how to remove history;
deleting a current file alone does not remove its history.

## License, reporting and CI

Project code uses [MIT](../LICENSE), a permissive license requiring retention of
the copyright and license notice. Third-party components retain their licenses;
this grants no rights to Roblox/Microsoft trademarks or proprietary services.
New installer payloads and the portable ZIP include LICENSE in their hashed
manifests. Existing release assets are unchanged.

[SECURITY.md](../.github/SECURITY.md) provides safe reporting guidance without
publishing a personal email address. GitHub private vulnerability reporting is unavailable
for this private repository (its API returned 404); enable and verify the
[private reporting route](https://github.com/LancePerdue23/roblox-multi-instance/security/advisories/new)
after making the repository public. Until enabled, there is no configured private
reporting channel; reporters are instructed to withhold public details.

Bug and feature issue forms are configured, with security reports directed to
the GitHub private reporting route. Windows CI uses Windows Server 2025, .NET 10, Node 24 and
Edge browser fixtures. It builds the desktop, runs badge, desktop/frontend,
service and shared-helper fixtures, packages unsigned validation output, and
checks distribution contents without installation. It has contents-read
permission and no publishing/deployment credentials. Local workflow files take
effect only after the maintainer commits and pushes them.

## Remaining publication steps

Local validation passed: three badge regression tests, the existing package-count
fixtures, all five YAML files parsed, actionlint for both workflows, a fresh
unsigned package build, and distribution verification for both installers and
the portable ZIP (including license hashes). No installation or Roblox launch
was performed. The full desktop/shared test suites are configured in CI but were
not rerun locally for these documentation/workflow/packaging changes.

- Review and publish the intended source changes, including required new files;
  this checkout contains substantial pre-existing uncommitted development work.
- Require a successful first hosted Windows CI run. Local checks do not establish
  clean-runner success or complete Windows/Roblox acceptance.
- Complete the documented Windows acceptance matrix and decide on unsigned
  distribution versus configuring a publisher identity before stable promotion.
- Reconfirm history/assets and service configuration immediately before any
  visibility change. Making the repository public exposes historical releases
  and their source tags as well as the current tree.
- After visibility changes, enable private vulnerability reporting, verify both
  live badges, and confirm anonymous release access. Public visibility alone
  does not make a prerelease a stable updater offer.

References: [Shields Release](https://shields.io/badges/git-hub-release),
[Shields all-asset Downloads](https://shields.io/badges/git-hub-downloads-all-assets-all-releases),
[GitHub private reporting](https://docs.github.com/en/code-security/how-tos/report-and-fix-vulnerabilities/configure-vulnerability-reporting/configure-for-a-repository),
[MIT license guidance](https://choosealicense.com/licenses/mit/).
