# Project guide

## Purpose and layout

MayaX-Battery is a Windows utility for the LAMZU Maya X. The GitHub repository is named `Lamzu.MayaX-Battery`; the application assembly is named `MayaX-Battery` and its namespace is `MayaXBattery`. It uses .NET 10 and C# 14, WPF for the details window, WinForms for the tray, and native HID calls for battery reads.

- `src/Program.cs`: startup, single-instance behavior, command-line options and preview commands.
- `src/TrayApp.cs`: polling, tray lifecycle and window coordination.
- `src/DetailsWindow.xaml` and `.xaml.cs`: details window.
- `src/TrayMenuRenderer.cs`: menu rendering and layout.
- `src/Battery.cs` and `src/Hid.cs`: protocol and Windows device access.
- `src/RuntimeEstimate.cs`: observation history and runtime estimate.
- `tests/Program.cs`: executable regression checks, not an xUnit or NUnit project.
- `tools/Build-Release.ps1`: self-contained, single-file Windows x64 package and smoke check.
- `.github/workflows/`: CI and tag-triggered draft releases.

Read the relevant files and current diff before changing them. Keep unrelated edits intact. Do not reorganize the project as a side effect of a small task.

## Behavior to preserve

- A normal launch opens the details window and starts the tray icon. Closing the details window leaves the tray running; Exit ends the application. A second normal launch brings the existing window forward. Startup shortcuts use `--tray` to leave the app in the tray; repeated `--tray` launches must not open a window. Keep ordinary launches without this argument.
- An unreadable device is not evidence that the user is inactive. Do not convert failures into zero-percent readings.
- Only battery reads are implemented. Do not add pairing, firmware, EEPROM or configuration commands without a specific request and protocol evidence.
- Hardware support beyond Maya X must be described as unverified until tested on the actual device.
- Runtime estimates use observed discharge, not user activity. Preserve the gaps, charge boundaries and sensor-jitter handling covered by tests.
- Keep history and language preferences under `%LOCALAPPDATA%/MayaX-Battery`. Migration from `%LOCALAPPDATA%/MouseBattery` must preserve the originals and copy only files missing at the new location. Tests and previews must not overwrite the user's data or contact HID devices.
- UI text must work in English and Russian. Keep device names and protocol identifiers independent of the selected language. Render country flags explicitly rather than depending on emoji support.

## Validation

Use Windows and the SDK in `global.json`. From the repository root:

```powershell
dotnet build src/MayaX-Battery.csproj -c Release
dotnet run --project tests/Checks.csproj -c Release
```

If the environment already uses RTK, `rtk dotnet build` is acceptable. Recover complete diagnostics after a wrapper error or truncated result. Do not install RTK or change its settings for this project.

For packaging changes, run `./tools/Build-Release.ps1 -Version 0.0.0-dev`. For UI changes, generate and inspect both languages at normal and enlarged scale. `dotnet test` does not run this project's console checks. Never claim hardware or interactive validation from a screenshot alone.

Non-Windows environments can review source and documentation but cannot establish native Windows runtime behavior. Report that limitation rather than weakening the checks.

## Documentation and delivery

Write maintained documentation and project instructions in English. Use natural, concrete wording and avoid em dashes. Keep README screenshots in English and label demonstration readings. Do not replace real UI screenshots with generated mockups.

Commit only files needed for the task. Exclude `bin`, `obj`, `artifacts`, local IDE state, credentials and device dumps. Follow the owner's instructions for commits and remote writes. Rewriting history, creating releases and pushing tags require specific authorization for that action.

## Focused procedures and delegation

Read only the procedure needed for the task:

- UI text, language settings, menu alignment or screenshots: `.codex/skills/maya-ui-check/SKILL.md`.
- CI, ZIP packaging or release verification: `.codex/skills/maya-release-check/SKILL.md`.

These paths are explicit project instructions, not implicit Markdown imports. Open the matching file before following it. They also work with agents that do not automatically discover skills.

Optional project roles live in `.codex/agents/`. Assign disjoint files to implementation and documentation workers. Reviewers remain read-only. Serialize builds, tests, previews and device access. The primary agent owns integration and final verification; delegate only when it improves the task.

## Owner deployment

When the owner requests an installation update, keep the owner's separate installed copy current with the latest verified build, and update its startup shortcut to the installed executable. Do this only after the software changes are verified and the destination is available. This is an owner-specific task, not a requirement for contributors.