# Helper interactive CLI

Installed packages register `rmi` on PATH. Open a new terminal after installation and run `rmi`. For development, run `./rmi.ps1` from the repository; it builds with the bundled SDK and opens the CLI without installation. Arguments work too: `./rmi.ps1 --help`.

The menu offers Launch Roblox, browser Play integration on/off, status, and exit. Launch automatically handles the singleton event through the existing on-demand worker. With integration enabled, use Play on Roblox as usual; the CLI does not need to stay open. Enter `q` to exit. No startup update checks, animations, or extra continue prompts run in the menu. Integration changes retain their confirmation prompt.

Direct commands remain available through `rmi --help`. Human results use readable labeled fields; `--json` retains structured output and exit codes for scripts. With redirected input or output, bare `rmi` displays help and `rmi menu` returns usage code 2 without starting an interactive session. Export destinations must be new files. The helper's browser URI and worker entrypoints remain windowless.
