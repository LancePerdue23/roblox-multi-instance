# Preview design audit

## Findings and fixes
- Expanded browser select menus did not reliably inherit the theme. Replaced all four select locations (arrangement, saved preset, monitor assignment, activity filter) with a shared select-only combobox and portal popup. Popup backgrounds, selected/hover states, text, borders and focus use the existing theme tokens. Cards cannot clip the popup.
- Dropdowns support arrows, Home/End, Enter/Space, type-ahead, Escape, Tab and outside dismissal, selected-state announcements, disabled states and viewport positioning.
- Title-bar colors were independent from app appearance. WindowTheme now follows saved appearance immediately after save/reset/import and Windows preference changes. Native caption colors are used when available; WPF WindowChrome provides a themed fallback if native color support cannot be verified. Caption controls retain minimize/maximize/restore/close, caption dragging, double click and resizing. Native Windows snap-hover UI remains dependent on native caption support.
- Tray menu now uses the same palette. High contrast returns to system colors.
- Tabs now support arrow/Home/End keyboard navigation. Scrollbars, text selections, focus outlines and active navigation hover use the shared theme. Popup scroll position is independent from page scroll; menus close if the host page moves.
- Restored the exact default WinForms window icon used by the old app, saved to host/app.ico using Form.Icon. Embedded it into the executable, WPF window and tray; no artwork generated and no old files changed.

## Verification
- preview/Test.ps1 passed: 16 host boundary checks, 26 backend checks, 4 bridge unit tests, 5 browser workflow/audit tests, generated contract drift and production TS/Vite build.
- Browser audit covers all five pages, narrow layout, dropdown palettes across Dark/Light/Warm neutral/Follow Windows, light OS mode, forced colors, keyboard selection/dismissal, tab navigation and dialogs. Screenshots under .build/preview-ui-results/.
- Published host built without warnings. Native --smoke passed. This environment rejects native caption color queries, so the themed fallback was exercised. Captured and visually reviewed dark/warm title bars with icon and controls; the smoke also exercises maximize, restore and minimize. Theme caption screenshots live under .build/preview/host-chrome-*-smoke.png.
- Reviewed expanded dark/warm dropdowns, System light surface, and high-contrast popup screenshots.
- Original 80 source files and release executable still match the preserved baseline.

## Remaining acceptance boundaries
Real high-DPI multi-monitor interaction, OS preference changes triggered through Windows Settings, screen-reader interaction, and native Windows caption rendering on a compositor supporting the DWM attributes remain hardware/manual checks. Existing live Roblox and installer acceptance boundaries are unchanged. No app settings were changed by fixture or caption smoke checks.

Native color attribute reference: https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
