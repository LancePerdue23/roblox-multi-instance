# Launch games

The global **Launch games** button opens a compact launch panel on every desktop page. Opening the panel only reads account metadata, recent games, and saved layouts. The local account read skips avatar requests. No launch occurs until the final review action.

- **Repeat last launch** reviews the newest recent game's ordered account IDs and layout. Removed accounts, older entries without accounts, and missing or changed saved layouts require editing first.
- **Edit setup** and **New launch** let users choose a game link or Place ID, accounts in slot order, and a layout. Public server Job ID is optional. Layouts include Grid, Columns, saved presets, and leaving windows alone.
- **Review launch** re-reads accounts and layouts and validates the game through the existing target resolver. The final button names the number of accounts. Saved-account launching remains subject to the existing build and helper gates.
- **Open Roblox app** retains the original app-only action as a secondary choice.

Launches use the existing `accounts.launchBatch` operation. Progress, partial results, unknown outcomes, and sign-in requirements are shown in the panel. Hiding it leaves the batch running, and reopening it retains the current result. Canceling requests cooperative cancellation; started clients remain open. A short transition guard prevents the second click of a launch double-click from activating a newly positioned navigation button.

Recent-game history keeps its existing newest-first, per-game behavior. The optional `Arrangement` field remembers Grid or Columns across restarts; older history remains readable. Explicit public server IDs are used for that launch only. Repeating a recent game uses automatic matchmaking. The Play page also reads and repeats these quick layouts. The launch panel has its own draft, so editing or canceling it does not replace the Play page's draft.

Grid and Columns reuse the existing placement coordinator on the primary display. Both require the same complete, verified account-to-window matching as saved layouts. Only this batch's process lifetimes are eligible; existing unrelated clients are excluded. Layout reporting checks that every batch window was placed and does not claim full success when a fixed or unavailable window was skipped.

Browser fixtures cover review-before-launch, account order, preserving the Play draft, removed accounts, preset slot counts, target rejection, double-clicks, background progress, cancellation, and narrow layouts. Backend fixtures cover layout validation and history persistence. Live multi-account gameplay acceptance remains separate; fixture tests never launch or stop real Roblox.
