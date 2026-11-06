# Local account sessions and launch feasibility

## Development update: native launch provider implemented, live acceptance pending

The desktop now wires BrowserAccountSessionProvider into the saved-account batch
flow. Launch selected is enabled by the native host and requires an explicit
testing confirmation. Non-host/fixture AccountManager callers remain unavailable
by default. The historical sections below describe the earlier groundwork.

For each selected account, a non-navigating WebView2 controller opens its existing
isolated profile. Native authorization verifies the immutable user ID, requests a
client assertion and one-time authentication ticket, and rechecks identity before
handing a freshly constructed player URI to the existing signed-player/helper
launch service. Redirects are refused; challenges, expiry and throttling stop that
account. Only a CSRF rejection gets one retry, before any player handoff. Rotated
session cookies are written back to the same browser profile and checked before
continuing, preserving the server's expiration. No vault migration or duplicate
session file is introduced.

After handoff, a Started result requires one fresh trusted process/window, a log
currently held open by that process, the selected numeric user ID in the last
GameJoinLoadTime record, and the unique launch attempt GUID. Unavailable or
ambiguous evidence reports Unknown, leaves games alone and skips automatic
layouts. A unique process or singleton-helper completion alone is insufficient.
This correlation rule still requires controlled live validation against the
current player's actual logs. Unsupported log formats may leave a successful
game launch unconfirmed; the UI explicitly asks the user to check before retrying.

HTTP fixtures use synthetic sessions/tickets and no network. Run the account
fixture project and frontend feature workflows for focused checks. Live acceptance
still requires the owner to select a public test Place ID, launch one saved account,
verify its in-game identity, then repeat with two different selected accounts while
the normal browser/player account differs. Check expiry/reconnect, cancellation
and whether results actually reach Started. No real account/game acceptance has
been performed by these fixtures; automatic-layout readiness is not established.

Protocol research references (compatibility leads, not a stability guarantee):
[Roblox auth API index](https://create.roblox.com/docs/cloud/reference/domains/auth),
[RoALT native auth implementation](https://github.com/inspectsoftware/RoALT/blob/main/src-tauri/src/roblox_api/mod.rs),
and [RoALT player URI research](https://github.com/inspectsoftware/RoALT/blob/main/src-tauri/src/launcher/uri.rs).
The provider is independently implemented in C#; competitor code was not copied.

The desktop now has local account metadata, an isolated Roblox website sign-in
window, native identity verification, a protected-secret vault interface, strict
game target parsing, and a sequential batch coordinator. The production account
launch adapter is explicitly unavailable. No real Roblox credentials or games
were available for live acceptance, so this work does not establish that an
account can be launched into a game or that two accounts remain in game together.

## Implemented boundaries

- Accounts are local to the Windows user. React receives opaque local IDs,
  usernames, nicknames, and status. Numeric Roblox identity and browser profile
  paths stay native. A saved session is not presented as continuously verified.
- Add/reconnect opens a separate WebView2 user-data directory beneath
  LocalAppData/RobloxMultiInstance/accounts/profiles/<opaque-id>. The existing
  desktop WebView remains restricted to desktop.local. The sign-in view has no
  virtual-host mapping, host objects, web-message bridge, injected script,
  password autosave, downloads, or granted browser permissions.
- Top-level navigation and popups only permit HTTPS roblox.com or www.roblox.com
  on port 443 with no userinfo. HTTPS challenge frames may load third-party
  providers, but cannot access the desktop bridge or local files. Third-party
  sign-in, popup-dependent flows, and device/passkey permissions may therefore
  be unavailable; no challenge is bypassed. Use supported Roblox sign-in methods
  inside this surface or cancel.
- Clicking Verify sign-in reads the session cookie only in native code and
  requests users.roblox.com/v1/users/authenticated directly. The request does
  not follow redirects, has an eight-second deadline and an 8 KiB response limit,
  and never sends session material to the project maintainer. A changed account
  or duplicate saved account is rejected. The response verifies identity only,
  not game-launch permission.
- Canceled sign-in clears the entire isolated browser profile before returning.
  Failed/unsaved adds remove the unsaved directory; a locked profile surfaces a
  cleanup error rather than silently claiming removal. Remove deletes only the
  selected local profile and app-managed vault entry; it is not a global Roblox
  logout/revocation. Local profile files may remain briefly locked by WebView2
  processes; close the app and retry if removal fails.
- The optional native vault uses Windows DPAPI CurrentUser. No cookies, launch
  tickets, password material, numeric Roblox identity, or private links belong in
  telemetry or diagnostic exports. The vault is groundwork; current sessions
  remain in WebView2's browser profile instead of duplicated app files.
- Account metadata, profile, and vault paths reject reparse points in every
  existing ancestor and final file before reads, writes, deletion, or opening
  sign-in. Redirected or network storage is unavailable. Account directories and
  secret/metadata files grant access only to the current Windows SID, SYSTEM,
  and administrators; child browser storage inherits the private profile ACL.
- An exclusive operation.lock file lease serializes changes across app
  instances, including while an asynchronous sign-in window is open. Other
  instances can read the last committed state; changes wait cooperatively and
  can be canceled. Each write starts from metadata read after acquiring the
  lease, preventing concurrent app windows from losing each other's accounts.

## Inputs, batches, and layouts

V1 accepts a positive Place ID or an HTTPS Roblox /games/PlaceId game detail URL
without query parameters or fragments. An optional Job ID is a nonempty UUID.
Job ID alone is insufficient. Opaque share links, private-server links, arbitrary
Roblox schemes, and arbitrary executable/URL forwarding are rejected.

One batch action freezes account order and target, then launches accounts
sequentially through IAccountSessionProvider. Slots keep their original numbers
when an earlier account fails. Each provider outcome must include a verified
positive process ID and creation time to claim Started. The same process cannot
satisfy two slots. Only a fully successful, uncanceled, distinctly correlated
batch permits a subsequent layout step. The desktop freezes the chosen preset,
revalidates each PID and creation time through trusted discovery, requires exactly
one window per account, and routes those windows through the existing saved-layout
planner. Missing, ambiguous, or changed windows prevent automatic placement. This
integration is prepared but cannot run in production while the live launch
provider remains unavailable.

Cancellation stops pending launches and leaves started games alone. Cancellation
while awaiting a provider marks that observation unknown. Exceptions are
sanitized, and launches are never automatically replayed: the player may have
started before an error. Existing browser launches and layout presets are
unchanged.

## Why OAuth is not a player launch solution

[Roblox OAuth implementation](https://create.roblox.com/docs/cloud/auth/oauth2-develop)
supports public clients with PKCE and Open Cloud API authorization.
[The OAuth reference](https://create.roblox.com/docs/cloud/auth/oauth2-reference)
defines identity and resource tokens; it does not establish an API for logging
the installed player into a selected account. No OAuth token is substituted for
a game-launch session. OAuth also has age and publication requirements:
[overview](https://create.roblox.com/docs/cloud/auth/oauth2-overview),
[registration](https://create.roblox.com/docs/cloud/auth/oauth2-registration).
OAuth is not used by this session groundwork.

[Roblox deep-link documentation](https://create.roblox.com/docs/production/promotion/deeplinks)
describes Place ID, Job ID, and private link parameters, but deprecates the old
deep-link process in favor of share links.
[Share links](https://create.roblox.com/docs/production/promotion/share-links)
can only be created for experiences the creator owns, so they cannot be the only
input for a general-purpose game launcher. A historical authentication-ticket
flow is not assumed to be a currently supported public API.

[Microsoft's profile isolation documentation](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/multi-profile-support)
establishes browser storage separation.
[DPAPI](https://learn.microsoft.com/dotnet/api/system.security.cryptography.protecteddata)
protects app-managed secrets at rest for the Windows user. It is not protection
against malware running as that user.

## Manual gate before enabling a production launch provider

Use owner-controlled accounts in a disposable Windows profile; fixture tests
must never launch/stop Roblox or install the helper.

1. Verify Roblox sign-in, MFA, supported passkeys/challenges, cancel/close during
   initialization, identity mismatch, duplicate accounts, session expiry,
   reconnect, and restart. Check each profile remains isolated and failed adds
   clear their data.
2. Identify an acceptable Roblox authentication/launch mechanism. Record the
   current endpoints and compatibility limits. If no supported mechanism exists,
   keep automatic account launch unavailable and document that outcome.
3. Launch two distinct accounts into a test experience. Verify both remain in
   game together; repeat with the normal player's signed-in account changed.
   Never accept launching the wrong account as success.
4. Prove account-to-process/window correlation across player handoff, external
   browser launches, process-ID reuse, an expired launch authorization, partial
   failure, and cancellation after process start. Singleton-helper completion
   alone does not prove account identity.
5. Route only correlated windows through the existing layout preview/apply
   service. Verify stable account slots, missing monitors, DPI changes,
   fullscreen preparation, missing windows, and partial batches.
6. Seed secret/private-link canaries and inspect all logs, reports, bridge
   messages, telemetry payloads, and update requests. No account/session
   information may cross to the developer. Verify Remove and canceled adds clear
   only their own profile; document Roblox-side revoke/sign-out separately.

Run account fixture tests with the repository's local SDK:

    .build/sdk/dotnet.exe build src/desktop/account-tests/Account.Tests.csproj -c Release -m:1
    .build/sdk/dotnet.exe run --project src/desktop/account-tests/Account.Tests.csproj -c Release --no-build

The fixtures cover strict targets, native sign-in navigation policy, corruption
preservation, local identity consistency, DPAPI roundtrip/plaintext exclusion,
sequential batches, stable slot numbering, duplicate/unconfirmed processes,
failure sanitization, cancellation, and no replay. They cannot prove live Roblox
compatibility.
Additional Windows fixtures verify private ACLs, independent-manager
serialization and canceled lock waits, and reparse rejection for profile
ancestors and vault files. Symbolic-link fixtures explicitly report a skip if
the Windows account cannot create links; they are not assumed to pass silently.

## November 3 implementation and storage decision

Account metadata v2 now adds a group, local notes, optional color, favorite flag and
saved list order. Search/filtering happens locally. Read-only access accepts v1
without rewriting it; the first successful mutation retains the exact v1 original
as accounts.json.v1.bak before the atomic v2 replacement. Existing account IDs,
immutable Roblox identity, profiles and DPAPI entries remain intact. Reconnect
preserves organization. Full-list reordering rejects a stale/incomplete list, and
all changes retain the existing cross-instance lease and private storage checks.
Notes are ordinary metadata, not a secret store. The bounded metadata file limit
is 1 MiB for at most 100 accounts.

Native AccountSessionValidator now owns the bounded authenticated-identity GET
and reports Valid, Expired, Challenged, RateLimited, Unavailable or WrongIdentity
with a check timestamp. The sign-in window uses this service. These outcomes
reflect that request, not presence, launch access or continuing session validity.
The standalone individual/bulk background session-check UI is still pending:
current reusable cookies live in WebView2 profiles, not the optional native vault.
No new cookie duplication, application lock, master-password vault or backup is
implemented by this slice.

### Local competitor-source research (not live compatibility evidence)

Read-only inspection used downloaded snapshots RoALT commit
4d250c0565f5e6daea99797d591c83f76108b916 and MultiAlt commit
12863fd3251265008f63459071e80bf1692d09de. No competitor code was executed or copied.
RoALT's src-tauri/src/roblox_api/mod.rs and launcher/mod.rs show a native cookie
flow: optional client-assertion request, POST authentication-ticket, ticket response
header, then a constructed player URI and installed-client handoff. Its HTTP layer
handles CSRF retries, challenge responses, throttling and cookie rotation; launcher
code rereads rotated session material before authorization. MultiAlt's
src-tauri/src/api/auth.rs likewise requests authentication tickets. These are
research leads from third-party source, not a supported API contract or proof that
they work with current accounts/player builds.

A native-vault representation is technically plausible for a provider using such
a cookie/ticket mechanism, but remains **undecided for migration** until controlled
acceptance proves authentication, challenge behavior, rotation, authorization
expiry and trusted account-to-process binding. Do not migrate or retire persistent
browser profiles based on source inspection. A future provider must commit rotated
material safely, avoid replay after uncertain starts, validate the signed installed
player/handoff, and prove distinct selected identities while the ordinary player's
account differs. Existing production LaunchAvailable remains false. The current
browser-assisted Play action does not select saved accounts.

Implemented: organization and native sign-in validation extraction. Enabled in
production: metadata controls and extracted validator; saved-account launching
remains disabled. Live verified: none of the selected-account launch mechanism,
vault migration, rotation or process-binding requirements. The manual gate above
continues to apply.
