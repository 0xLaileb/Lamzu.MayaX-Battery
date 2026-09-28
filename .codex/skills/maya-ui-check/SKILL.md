---
name: maya-ui-check
description: Verify MayaX-Battery language switching, settings behavior, WPF layouts, tray menus and documentation previews after UI changes.
---

# MayaX-Battery UI checks

Read the changed UI code and `src/Program.cs` preview arguments. Keep the dark slate and mint palette unless the task asks for a redesign.

- Check English and Russian text, including errors, history notes, tooltip text, accessibility names and the single-instance message. Numeric formatting must follow the selected language.
- Language selection should update the open window and tray immediately. Confirm preference persistence with disposable settings files, including corrupt JSON, unsupported language values and inaccessible storage. Do not rewrite the real user's settings for a test.
- Compare menu text alignment, hover insets, separators, flags and submenu arrows. Inspect a long status label and both language names. Country flags must render without relying on platform emoji fonts.
- Run the console checks. Generate previews at scale 1 and 2 with explicit `--language en` and `--language ru`; use ignored `artifacts/` paths for extra samples. Preview mode must not access HID or persist settings.
- Inspect the actual PNG files. Check for clipped text, overlapping controls, poor focus/selection contrast and inconsistent spacing. A larger render scale is not proof of physical per-monitor DPI behavior.
- Update the tracked `docs/` previews in English when the visible UI changes. Label sample values as demonstration data in the README. Do not use generated artwork as proof of the real UI.

Report the commands and behaviors actually verified. Coordinate native windows and builds with other workers; do not interfere with an unrelated running application.