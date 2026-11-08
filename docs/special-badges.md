# Special account recognition

Alpha Tester recognition is cosmetic. Circle and Ribbon are the two appearance choices; each badge kind keeps its own palette, animation and visibility preferences. Settings → Badges appears only for recognized accounts or the owner-local preview. Hidden badges remain customizable. Reduced-motion preferences override animation choices.

## Shared signed eligibility file

One schema-2 `.rmibadge` file is issued per badge kind. It contains the product, badge ID/title, a random 32-byte salt (hex encoded), and SHA-256 eligibility hashes. The hash input is UTF-8 `rmi.badge.v2`, a newline, the salt, a newline, and the numeric Roblox user ID. The entire payload is signed using the existing ECDSA P-256 issuer and SHA-256; the application embeds only its public verification key.

Hashes obscure the membership list but do not guarantee anonymity: numeric IDs can be guessed and checked against the public salt and hashes. Editing the salt, membership or badge invalidates the signature. Existing schema-1 individual grants remain supported. New shared files require this updated app; older builds cannot claim them.

## Owner workflow

The existing private issuer, assignment list and outputs are under ignored `badge-owner`. Do not commit or package that directory. The issuer is DPAPI protected to the owner's Windows account. A copy of its protected file alone is not a portable backup; preserve the original Windows profile and arrange a recoverable secure backup before broad distribution. Never replace an already distributed verification key.

1. Edit `badge-owner/assignments.json` with stable numeric Roblox IDs, stored as strings.
2. Run `./scripts/Issue-SpecialBadges.ps1` under the issuing Windows account.
3. Distribute **only** `badge-owner/grants/alpha-tester.rmibadge` for Alpha Tester recognition. The same file works for every eligible tester. No private key or plaintext assignment list is distributed.
4. Regenerate and redistribute the shared file when adding eligible accounts. Old individual output files are not automatically removed; they are not the new shared file.

The issuer refuses to initialize over an existing issuer/public key and checks that the private key matches the app's public key before signing. Do not run `-Initialize` on this existing project.

## Tester workflow

Install or update the desktop app, then double-click the `.rmibadge` file. Desktop setup registers a per-user file association with a quoted direct app invocation, without overriding another application's extension default. If Windows has another association, use Open with → Roblox Multi Instance. Development builds can also open a full file path as their sole argument or use `--claim-badge "C:\path\alpha-tester.rmibadge"`.

The file is signed data, not an executable. Opening it starts the app (or forwards the bounded path to the existing same-user instance), waits for the renderer to be ready, verifies its signature, and opens the native Roblox sign-in window. No manual import in Accounts is needed.

The sign-in window verifies Roblox identity. Eligibility is checked before adding an account or saving recognition. Eligible new accounts are saved automatically; existing accounts are not duplicated or have their established profiles replaced. Wrong-account sign-in, invalid files and cancellation preserve the original file and grant nothing.

After verification the app saves a Windows-user-protected local record, reads it back and validates it, and only then deletes the delivery file. Failed deletion is explicitly reported for manual removal. Repeat claims are idempotent, delete the duplicate delivery after a verified save, and report “already claimed” without replaying unlock. Recognition remains stored independently of the delivery file.

## Limits and acceptance

There is no online badge server, remote revocation or expiry. Removing an account from a new shared file does not invalidate an older signed file or existing recognition. A retained or redownloaded file can be opened again, but does not bypass Roblox identity verification. A new PC/Windows profile needs another copy and sign-in.

New assignments and generic badge kinds need only a new signed file; new artwork needs an app update. The local role switcher is independent of authorization and excluded from public builds.

Automated fixtures cover signature tampering, eligibility, legacy compatibility, protected storage/restart, repeat claims, deletion failure, canceled/ineligible/new/existing sign-ins, bounded native activation, and association ownership. They do not establish successful real Roblox sign-in or Windows Explorer behavior on a clean tester installation; verify those manually before broad distribution. No real Roblox client or installer should be launched merely to run fixtures.
