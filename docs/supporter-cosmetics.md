# Supporter heart artwork

The shipped supporter system uses Ember (rose enamel and pulse), Orbit (faceted lilac heart and comet), and Radiant (golden winged heart and sunburst). These are the only artwork options in v1.2.0. The old Classic picker, palette controls, heart glyphs and animation styles are excluded from production JavaScript and CSS. The previous implementation is preserved as nonbuilt [historical source snapshots](history/supporter-classic/README.md).

Existing saved Classic or Detailed selections use the new artwork automatically. A saved animation preference is retained; reduced-motion preferences are always respected. Appearance choices never change supporter entitlement, receipts or verified contributions. The new artwork uses fixed tier colors, so no palette controls are offered.

The Support page offers all three tiers for viewing. Unlock replay is available only at or below the verified tier. The supporter row and default faceted-heart invitation are 44 pixels high and align with the navigation column; recognition badges appear above them. The invitation does not imply earned supporter status.

The owner-only preview shell can explore every tier, idle/hover and staged unlock animations in Dark, Light and WarmNeutral. It uses separate preview preferences and never grants recognition, starts checkout or launches Roblox. Run the ignored Preview-Supporters launcher or use the development build's --supporter-preview entry point. Normal packages exclude the unrestricted route, role switcher and developer credit controls.

USD tier thresholds remain $3 / $7 / $15, with cumulative signed receipts after separate service deployment and sandbox acceptance. Robux recognition independently accumulates approved pass credit at 300 / 700 / 1,500. The higher verified currency tier supplies cosmetics; totals are never combined or converted. See [cumulative contributions](supporter-cumulative-contributions-plan.md).

Build with ./Build.ps1. Production-bundle exclusion, saved preference migration, entitlement, reduced motion and sidebar layout are covered by the supporter browser fixtures. Local preview fixtures verify the same new artwork behind the owner-only build flag.
