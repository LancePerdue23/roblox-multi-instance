# Isolated Windows release acceptance

Status: packaged matrix NOT RUN. For v1.2.0, the owner reports successful development-build testing, has no VM for packaged acceptance, and requests unsigned packages for the next testing stage. No installation is performed by the release checks. Fixture tests, signatures and embedded payload inspection do not establish installed behavior or game compatibility.

Use a disposable Windows VM or an otherwise isolated test machine. Do not use the owner's active account as a substitute. Take a recoverable snapshot and use test accounts with no production credentials. Keep release artifacts and evidence outside the VM before resetting it.

## Read-only preparation

- Record artifact SHA-256, product version, source revision and whether the checkout was dirty. Record OS version, WebView2 runtime, monitor/DPI configuration and VM snapshot.
- Run `Test.ps1`, `Test-Shared.ps1`, `Package.ps1` (or the signed release command in publisher-signing.md) and `scripts/testing/Test-Distribution.ps1` (with `-RequireSigned` only for candidates intended to be signed). The owner has chosen unsigned v1.2.0 validation packages.
- Confirm public UI contains neither local build time nor supporter role tools. Record signature publisher and certificate fingerprint for signed candidates.

## Manual matrix

Record each row as Pass, Fail or Not run, with artifact hash, observed behavior and an evidence path. Keep secrets, account sessions and launch tickets out of logs/screenshots.

| Case | Required observation | Result |
| --- | --- | --- |
| Desktop fresh install | Correct payload, explicit integration choice, owned shortcuts/PATH/uninstall entry | Not run |
| Helper fresh install | No desktop payload; idle helper is not resident | Not run |
| Upgrade, retained data and reinstall | Settings/history retained; numeric downgrade and edition mismatch refused | Not run |
| Canceled elevation/interrupted setup/locked files | Recoverable outcome; no false success or damaged prior installation | Not run |
| Browser protocol conflicts, repair and recovery | Ownership checked; original registration restored correctly | Not run |
| Uninstall, retain data | Only owned registrations/files removed; data retained | Not run |
| Uninstall, remove data | Explicit confirmation; only intended data removed | Not run |
| Edition switch | Requires uninstall and correct selected edition afterward | Not run |
| Real update handoff | Correct publisher/version/edition; graceful manager closure; games preserved | Not run |
| Invalid update trust | Unsigned, altered, wrong publisher and unknown/revoked trust refused before setup execution | Not run |
| Two-account game session | Correct accounts remain in-game; unknown launch outcomes do not cause duplicate launches | Not run |
| Priority/EcoQoS apply/reset/manager exit | Observed native values match; unrelated clients untouched; restoration verified | Not run |
| Client FPS/graphics integration research | Establish a supported, version-compatible mechanism before enabling application | Not run |
| Monitor/DPI/theme/accessibility | Layout restoration, keyboard/screen-reader behavior and native themes verified | Not run |
| Preview soak | Capture lifecycle, minimized/closed windows and resource usage remain bounded | Not run |

Do not mark public release accepted until applicable rows have evidence. FPS/graphics remain saved requests; they are not working process controls.
