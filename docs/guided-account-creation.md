# Guided account creation

Accounts → Create accounts opens native setup for a sequential signup queue. Preview the numbered usernames, supply an accurate birthdate, and choose unique generated passwords (default) or a shared password. Each signup uses a fresh profile and Roblox's official website. Users review and submit every form and handle challenges directly. No signup request or CAPTCHA solution is automated.

Autofill is scoped to recognized top-level Roblox signup fields. Unknown markup falls back to manual completion. Refill is explicit after initial autofill. Next username advances the suffix; Edit details changes the native supplied credentials. Final username and password confirmation is required before saving, including after manual edits. A saved session verifies identity, not password correctness.

Supplied credentials are encrypted with Windows CurrentUser protection in a separate credential store. Pending records are written before the signup browser opens and retained for uncertain or skipped attempts. Successful account metadata is saved before credential promotion; startup reconciles interrupted promotion. Existing accounts do not need migration. No passwords or birthdates travel through the desktop page bridge.

View credentials opens a native masked reveal/copy window. Copied passwords expire from the clipboard after 30 seconds only if unchanged. Unfinished signups allows credential viewing, explicit deletion, and ordinary sign-in recovery. Queues do not resume after restarting. Removing a saved account removes its associated credential record, but does not delete the Roblox account.

## Release acceptance

Fixture tests do not establish compatibility with the live Roblox signup form. Before release, manually verify signup on the current site using an accurate user-supplied birthdate: check all autofilled fields, submit once, complete any challenge, confirm final supplied credentials, and verify the account appears in the library with its session retained. Check Next username, Refill, Skip, Stop, credential reveal/copy, and recovery of an interrupted signup. Launch through the existing Play flow only as a separate deliberate check. No live signup has been performed by the automated tests.

November 7: the owner confirmed that live signup works. This accepts the signup flow only; install/update and multi-account gameplay acceptance remain separate. Partial autofill now stops automatic retries until explicit Refill, preserving subsequent manual corrections.
