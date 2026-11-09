# Private GitHub update access

Repository access in a browser is separate from the desktop updater's API requests. Version 1.1.1 adds an explicit native credential prompt and an opt-in validation channel.

1. Install 1.1.1 manually once. Older versions cannot discover private prereleases anonymously.
2. Create your own GitHub personal access token that can read this repository. Prefer a fine-grained token limited to this repository with Contents: read when GitHub permits selecting it. GitHub currently limits fine-grained tokens for repository/outside collaborators; a collaborator may need a classic token with the repo scope. Classic repo scope is broader than read-only. Use an expiration and revoke it when no longer needed. Never use the maintainer's token.
3. In System → Version & updates, choose Connect GitHub update access. Enter your username and token in the Windows prompt (the token goes in the password field).
4. Enable Include validation prereleases, then Check for updates. Review the release, download, and explicitly run setup.

The token is held in Windows Credential Manager for the current Windows user. It never enters the web page, settings JSON, reports, or logs. Credentials are attached only to the exact GitHub API endpoints for this repository; redirected asset downloads receive no authorization header. Disconnect removes the stored token. The validation-channel preference is retained separately. Credentials are intentionally retained across ordinary upgrades/uninstalls; disconnect or remove the RobloxMultiInstance/GitHubUpdates entry in Windows Credential Manager to remove them.

Authenticated checks inspect up to 100 published releases, exclude drafts, require explicit prerelease opt-in, and choose the highest numeric version for the installed edition. Helper release tags are interpreted only for the helper edition. Installation still requires the published digest/size, trusted URL, executable version, edition/protocol and embedded payload checks. An optional configured publisher pin remains enforced. No automatic download or installation is added.

Denied or missing API access produces guidance rather than an up-to-date claim. Expired/revoked credentials must be replaced. This does not bypass repository permissions or organization policies. Browser/manual release downloads remain available.

References: [GitHub token limitations and permissions](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens), [release API](https://docs.github.com/en/rest/releases/releases), [asset download API](https://docs.github.com/en/rest/releases/assets).
