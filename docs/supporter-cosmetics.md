# Supporter cosmetic previews

Double-click `Preview-Supporters.cmd`, run `./Preview-Supporters.ps1`, or launch `.build/desktop/RobloxMultiInstance.exe --supporter-preview`.

The regular desktop executable still opens the usual app without that argument. The preview uses a separate instance name, WebView profile, and isolated preferences folder, so both windows can coexist. The same current development build contains both entry points; there is no duplicate build to keep in sync.

Settings → Support and the owner preview shell offer a persistent Classic hearts / Detailed hearts design switch. Classic remains the default for existing settings and preserves its palette when switching back. Detailed hearts implements Ember (rose enamel and pulse), Orbit (faceted lilac heart and comet), and Radiant (golden winged heart and sunburst), with idle, hover/focus and staged unlock animations. Detailed artwork uses fixed concept colors and does not offer ineffective palette controls. The main badge, owner-local role dropdown and replay buttons use the selected artwork without changing entitlement.

The Classic shell displays all three proposed tiers: Supporter (solid heart), Supporter+ (gradient heart and glow), and Super Supporter (gradient, accent border, heartbeat and shimmer). Rose, Lilac, Ocean, and Amber palettes are included. Each tier has a replayable unlock dialog; animation can be disabled and respects reduced motion. Dark, Light, and WarmNeutral appearances can be checked in the shell.

These are cosmetic previews. They do not grant or persist a purchased tier, send checkout requests, launch games, or modify real account/supporter state. One-time USD tier prices are $3, $7, and $15. Stripe remains disabled by default. The 21 approved group passes now accumulate fixed Robux credit across connected accounts at 300 / 700 / 1,500 thresholds. Signed cumulative USD receipts and verified Robux tiers drive the main sidebar independently; legacy recognition retains base Supporter. The Support page also contains the style gallery for review.

Build with `./Build.ps1`. Relevant verification: `npx playwright test tests/supporter-studio.spec.ts tests/features.spec.ts` from `src/desktop/web` and the desktop host tests.

Cumulative USD support is implemented locally with recovery codes, a durable deduplicated ledger and signed tier/total receipts. See [the cumulative implementation](supporter-cumulative-contributions-plan.md) for $5 + $2 + $8 progression and required deployment/sandbox acceptance. Detailed sidebar rows fill the navigation column at 44px high; Classic styling is preserved.

In the sidebar, recognition icons appear above the supporter entry in both public and owner-local views. Detailed supporter entries use a compact 44-pixel height and size to their icon/text content; Classic retains its existing appearance.

The default Support development invitation also follows Classic/Detailed selection. Classic retains the plain heart; Detailed uses a faceted outline heart and mint sparkle with the same compact column fit. Recognition badges precede the invitation in actual status and owner Default previews. This invitation never indicates earned entitlement.

The local-supporters build adds Dev credit controls on Settings → Support. Set any nonnegative whole-number Robux credit, Apply, or Add selected pass to exercise shifted slider milestones and tier unlocks. Use real credit clears the simulation. The labeled preview is stored only in the local WebView preference and never changes verified ownership or receipts. Normal builds exclude it.
