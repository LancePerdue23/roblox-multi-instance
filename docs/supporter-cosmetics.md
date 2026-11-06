# Supporter cosmetic previews

Double-click `Preview-Supporters.cmd`, run `./Preview-Supporters.ps1`, or launch `.build/desktop/RobloxMultiInstance.exe --supporter-preview`.

The regular desktop executable still opens the usual app without that argument. The preview uses a separate instance name, WebView profile, and isolated preferences folder, so both windows can coexist. The same current development build contains both entry points; there is no duplicate build to keep in sync.

The shell displays all three proposed tiers: Supporter (solid heart), Supporter+ (gradient heart and glow), and Super Supporter (gradient, accent border, heartbeat and shimmer). Rose, Lilac, Ocean, and Amber palettes are included. Each tier has a replayable unlock dialog; animation can be disabled and respects reduced motion. Dark, Light, and WarmNeutral appearances can be checked in the shell.

These are cosmetic previews. They do not grant or persist a purchased tier, send checkout requests, launch games, or modify real account/supporter state. One-time USD tier prices are $3, $7, and $15. Stripe Price IDs and Roblox gamepass IDs remain unset. Existing Stripe and gamepass verification currently grant the base Supporter tier; higher-tier verification and applying higher-tier cosmetics to the main workspace will need the eventual tier-to-product mapping. The Support page also contains the style gallery for review.

Build with `./Build.ps1`. Relevant verification: `npx playwright test tests/supporter-studio.spec.ts tests/features.spec.ts` from `src/desktop/web` and the desktop host tests.
