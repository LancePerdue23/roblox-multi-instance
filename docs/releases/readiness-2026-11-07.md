# Release readiness — November 7, 2026

Historical assessment of the pre-release working tree. The owner subsequently completed the focused live checks and authorized preparation, commits, pushes and private prerelease publication as version 1.1.0. The version-1.0.1 hashes and prerequisites below describe the earlier assessment snapshot; current release details are in [v1.1.0](v1.1.0.md).

Decision, updated after owner confirmation: **ready to prepare a private validation prerelease**. The owner personally completed the focused live checks discussed in the review, and the intended tester has private repository access for manual downloads. Remaining publication preparation is versioning, source capture, release notes, and rebuilding/verifying the newly versioned packages. Public/stable acceptance remains separate.

This assessment covers the working tree based on `8f4e93e5fbf006ae078186143bd1879285ea7d38`, including uncommitted and untracked changes. The current product version remains 1.0.1. The public frontend is the normal production build; the machine-local role preview was excluded from packaging. No commit, push, installation, real Roblox launch/termination, or publication was performed for this assessment. Local `artifacts/` outputs were rebuilt as unsigned validation packages under the unchanged version; they are not a newly versioned release candidate.

## Verified evidence

| Check | Result | Scope |
| --- | --- | --- |
| `Test.ps1`, completed in the preceding implementation turn on this source | Pass | Desktop host/preview/backend checks, contracts, frontend build, 17 frontend unit checks, 99 browser checks; 7 local-preview-only checks skipped; account/support/service fixtures pass |
| `Test-Shared.ps1`, rerun for this assessment | 138/138 pass | Shared helper, process identity, cleanup, update, registry/ACL and placement fixtures; real Roblox integration omitted |
| `Package.ps1`, rebuilt for this assessment | Pass | Installer and portable desktop packages; unsigned validation mode |
| `Test-Distribution.ps1 -ExpectedVersion 1.0.1.0` | Pass | Installer hashes, editions, embedded allowlists/payload hashes, portable members and checksums; no installation |
| Packaged path scan | Pass for the checked paths | No `badge-owner`, issuer files, assignments, local role switcher or local preview marker in installer payload paths; not a claim of a comprehensive security audit |
| Focus/close-cancel audit journey | Pass | Mock backend only; verifies one-window focus and cancellation without a close request |
| All-destinations milestone audit journey | Fail: stale test | Waits for an Activity sidebar button; Activity is now a System tab. Evidence: `.build/release-readiness/browser-audit/browser.json` |
| Full milestone soak | Not run | The stale navigation assumptions also occur in the soak; repair the audit before using it as release evidence |

Fresh artifact SHA-256 values, all product version 1.0.1.0:

| Artifact | SHA-256 |
| --- | --- |
| RobloxMultiInstance-Setup.exe | `660ECEEA4B50804B7F0D012B29ADC2F7F20616383442F8B22C82C8C345A05E9F` |
| RobloxLaunchHelper-Setup.exe | `F74CD312931AB0CC6CF79110C87122BB5044DE7A55EAA0058B2CD94175A662C9` |
| RobloxMultiInstance-Portable.zip | `4762C97FAB524E1EDCCE31EAAB304BCBCBE33253C0DE2B4BA8DFEE8064AEBB32` |

## Publication prerequisites

1. **Choose and apply a new version.** `Directory.Build.props` still says 1.0.1, already published on GitHub. Reusing it prevents normal greater-than-version update selection and makes different builds indistinguishable by product version. Recommend 1.1.0 for the new account-creation, launch-panel, Stop All and recognition features, with GitHub's prerelease flag for validation. Do not add an unsupported `-beta` suffix to the numeric tag format consumed by the updater. Rebuild/check all packages after the version change.
2. **Capture the intended source.** Before this report was added, status showed 35 modified tracked files and 28 untracked files. Important new runtime sources, the badge verification public key, and tests are among the untracked files. A release from HEAD alone would omit these features. Review and explicitly commit the intended source before tagging; exclude owner-local badge issuer data and preview tooling. This assessment did not stage or commit anything.
3. **Prepare accurate release notes.** Existing notes describe older 1.0.1 behavior and cannot describe this candidate. Document quick launch and repeat/review, Grid/Columns/preset layout choices, Stop All's forced termination, signup/credential recovery, badges and update trust changes. Preserve the separate live-acceptance limits. Align README download links and release metadata with the chosen version.

## Update delivery needs a release decision

Owner decision for the current testing scope: the tester is a repository collaborator and can download private releases manually. Automatic update discovery is therefore not a blocker for this private prerelease. Repository access in a tester's browser does not authenticate the application's updater; the limitations below still apply before promising automatic updates.

GitHub metadata was read directly during the assessment:

- Repository visibility is private.
- The published desktop and helper releases are `v1.0.1` and `helper-v1.0.1`; both are prereleases, published November 5 Eastern time. Older 1.0.0 prereleases are also present.
- There is no stable latest release: authenticated `/releases/latest` returned HTTP 404. An anonymous request matching the app's access model also returned HTTP 404.
- The desktop release contains the desktop setup and portable ZIP. The helper setup exists only on the separate helper-prefixed release.

The current release source in `src/core/Updates.cs` requests `/releases/latest` without GitHub authentication and rejects drafts/prereleases. It accepts numeric `v` tags and selects the installed edition's installer from that one release. Therefore another private prerelease will not produce an in-app update offer. Making a release stable alone does not make private assets anonymously accessible. Do not embed an owner GitHub token in the application as a workaround.

There is an additional edition-layout mismatch if the same split publication layout is used for future stable updates: a numeric desktop latest release lacks the helper installer, and a `helper-v...` tag is rejected by the numeric version parser. A future auto-update channel needs both edition installers in the selected numeric release, or explicit edition-aware release discovery. Manual authenticated tester downloads can continue under the current private prerelease model. Publishing a stable release just to exercise the updater is not a substitute for stable acceptance.

## Live acceptance and known issues

The [isolated Windows checklist](windows-acceptance.md) remains marked **NOT RUN**. Fresh install, upgrade from the existing release, real update handoff, repair/uninstall, association ownership, retained data and edition switching have not been accepted on these artifacts. Automated payload checks do not establish them.

The owner confirmed live signup works, as recorded in [guided account creation](../guided-account-creation.md). This is useful scoped evidence and does not accept every signup recovery/credential action, clean-install badge activation, multi-account gameplay, or installation/update scenario.

The owner subsequently confirmed personally completing the focused live gameplay checks raised in the review: quick/repeat launches with different accounts, automatic layouts, cancellation, Stop All, and checking the close-to-tray condition. This is accepted as owner-performed manual verification for the current private tester scope, separate from automated test evidence.

The scenarios discussed were:

- Quick launch and Repeat last launch with two distinct real accounts; verify actual account identity, game/server behavior and Grid/Columns placement. Retry/unknown/cancellation must not duplicate launches or close already-started games.
- The reported one-window-X-closes-other-games interaction: logs implicate Roblox's close-to-tray replacement process. Retain this condition and the Roblox setting guidance in tester notes. The owner's overall live-check confirmation does not mean the application's code now changes Roblox's X-button behavior. See [closing multiple clients](../closing-multiple-clients.md).
- Stop All with explicit consent, including background clients and preservation of unrelated processes. Fixtures do not establish the observed game behavior.

Signup credential recovery and badge-file activation on a clean installation remain part of broader acceptance; the owner's confirmation is not being expanded into a claim that the full isolated Windows matrix was completed.

Signing remains unconfigured. These outputs have `Release=false` and no publisher pin/signature. That matches the current unsigned private-validation workflow; it does not satisfy the signed public release path in `Package.ps1 -Release` and `Test-Distribution.ps1 -RequireSigned`. The public/stable gate also requires the applicable manual acceptance evidence; signing alone is insufficient.

## Recommended next milestone

Prepare a **1.1.0 private validation prerelease** after the version/source/notes prerequisites and newly versioned package verification. Focused live checks are accepted on the owner's confirmation, and manual authenticated downloads fit the current tester arrangement. Repair and rerun the milestone audit, then complete the applicable isolated install/update/gameplay matrix before stable promotion. Publication remains a separate user action.
