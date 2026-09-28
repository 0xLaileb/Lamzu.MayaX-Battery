# 🖱️ MayaX-Battery

A Windows battery monitor for the [LAMZU Maya X](https://lamzu.com/products/lamzu-maya-x). It shows the current charge in the notification area and estimates remaining runtime from observed discharge.

![MayaX-Battery window showing the English interface](docs/window-preview.png)

![Tray battery indicator examples](docs/preview.png)

![English tray context menu](docs/menu-preview.png)

The screenshots use demonstration data. They do not show live readings from a connected mouse.

[![CI](https://github.com/0xLaileb/Lamzu.MayaX-Battery/actions/workflows/ci.yml/badge.svg)](https://github.com/0xLaileb/Lamzu.MayaX-Battery/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](global.json)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## Contents

- [Features](#features)
- [Getting started](#getting-started)
- [Usage](#usage)
- [Diagnostics](docs/diagnostics.md)
- [Adapt another mouse with an AI agent](#adapt-another-mouse-with-an-ai-agent)
- [How it works](docs/how-it-works.md)
- [Checks](#checks)
- [Project structure](#project-structure)
- [Contributing](#contributing)
- [License](#license)

## Features

| Feature | Details |
| --- | --- |
| Tray battery indicator | Shows the battery percentage with distinct states for low charge, critical charge, charging and unavailable readings. |
| Polling and recovery | Checks about once per minute. After a failed read, it retries after 15 seconds up to four times, then returns to a 60-second interval. |
| Details window | Shows battery status, the last check, polling interval and discharge history. Closing the window keeps the app in the tray. |
| Runtime estimate | Uses readings taken while discharging. An estimate appears after at least 30 minutes and a drop of at least 2 percentage points. Charging periods and long gaps are excluded. |
| English and Russian | Choose `EN` or `RU` with the flag buttons in the window or from the tray menu. The selection applies immediately and is saved locally. By default, the app follows the Windows display language when it is Russian; otherwise it uses English. |
| Local history | Stores observations and language preferences under `%LOCALAPPDATA%\MayaX-Battery`. Existing data under `%LOCALAPPDATA%\MouseBattery` is copied when needed; the original files are preserved. |
| Diagnostics | Saves a local ZIP with relevant HID metadata, candidate selection reasons and available polling events. Automatic collection sends no vendor commands and uploads nothing. It adds no battery support for other mice. |
| Standalone Windows app | The release is a self-contained, single-file Windows x64 executable. No separate .NET runtime installation is required. |

The project targets the LAMZU Maya X. A battery reading was checked on this model on September 3, 2026. Compatibility with other devices has not been established.

## Getting started

### Requirements

- Windows 10, version 22H2, or Windows 11, x64.
- To build from source: .NET SDK 10.0.401, pinned in `global.json`, with C# 14.
- A LAMZU Maya X mouse or receiver for live readings. The automated checks do not require a connected device.

### Download

Download the latest ZIP from [GitHub Releases](https://github.com/0xLaileb/Lamzu.MayaX-Battery/releases). Extract the archive and run `MayaX-Battery.exe`.

To create a local package from PowerShell at the repository root:

```powershell
.\tools\Build-Release.ps1 -Version 1.0.0
```

The package and its SHA-256 checksum are written to `artifacts/release/`. The script also runs an executable preview check without accessing HID hardware.

## Usage

Start `MayaX-Battery.exe` to open the details window and show the tray icon. Running it again brings the existing window forward. Use `--tray` to start with only the tray icon. Closing the window leaves the app running in the tray. Double-click the tray icon to reopen the window. Right-click it to refresh the reading, select a language, or exit.

To collect a report without opening the UI or tray, run `MayaX-Battery.exe --diagnostics`. It saves a uniquely named ZIP beside the executable. Exit code `0` means the ZIP was saved, `1` means export failed, and `2` means invalid arguments. A saved report may still show an incomplete device scan. The interactive **Export diagnostics...** action remains available from the window and tray menu and lets you choose the destination.

See [collecting a compatibility report](docs/diagnostics.md) for wired and wireless collection steps and the [universal prompt for an AI agent in a fork](docs/agent-adaptation-prompt.md). The report is a scoped, passive snapshot; firmware can remain unknown, and HID report IDs are not collected. To investigate another mouse, use a separate fork and require exact-model protocol evidence before implementation. If normal polling is unresponsive, `MayaX-Battery.exe --diagnostics-only` opens a separate passive UI instance without saving history or preferences.

### Start automatically on Windows 11

1. Extract the app into a permanent folder, such as `C:\Apps\MayaX-Battery`.
2. Press **Win + R**, enter `shell:startup`, and press **Enter**. This opens the startup folder for your Windows account.
3. Right-click an empty area in that folder and choose **New > Shortcut**.
4. Enter the executable path followed by `--tray`, for example: `"C:\Apps\MayaX-Battery\MayaX-Battery.exe" --tray`.
5. Name the shortcut **MayaX-Battery** and click **Finish**.

The app will start in the tray the next time you sign in. You can double-click the startup shortcut to check that it starts without opening a window. If Windows has disabled it, open **Settings > Apps > Startup** and enable **MayaX-Battery**.

Keep ordinary desktop shortcuts without `--tray` so they open the window. A repeated `--tray` launch leaves the existing window state unchanged. To stop automatic startup, delete the shortcut from `shell:startup`.

Windows controls which tray icons remain visible. To keep the battery indicator visible, open the hidden icons menu (`^`) and drag it onto the notification area. See [Microsoft's taskbar guide](https://support.microsoft.com/en-us/windows/experience/personalization/customize-the-taskbar-in-windows).

## Adapt another mouse with an AI agent

If you use an AI coding agent, [agent-adaptation-prompt.md](docs/agent-adaptation-prompt.md) provides a reusable workflow for investigating and adapting this app to your mouse in your own fork. The file contains only instructions for the agent, so you can point the agent directly to it without copying a prompt from a code block.

1. Fork this repository from `main` and open a local checkout of your fork in your agent.
2. Tell the agent: "Follow `docs/agent-adaptation-prompt.md` to investigate and adapt this fork to my mouse."
3. Provide your exact mouse model and diagnostic ZIP files when available. The agent will ask for missing details and can help collect reports or build the diagnostic executable.

The workflow covers source review, official protocol research, a compatibility plan, implementation in the fork, tests and a local build. A diagnostic report alone may not establish a safe battery protocol. Support for another model remains unverified until tested on that exact mouse and receiver; this upstream project still targets Maya X.

## Checks

Build the app and run the hardware-independent checks from PowerShell at the repository root:

```powershell
dotnet build src/MayaX-Battery.csproj -c Release
dotnet run --project tests/Checks.csproj -c Release
```

The checks run as a console program, not through `dotnet test`. GitHub Actions builds the app, runs these checks without a connected mouse, packages the application and smoke-checks the published executable.

## Project structure

| Path | Purpose |
| --- | --- |
| `src/` | Windows tray app, HID access, battery polling, runtime history and details window. |
| `tests/` | Hardware-independent console checks. |
| `tools/` | Release packaging and PowerShell utilities. |
| `docs/` | Screenshots, HID protocol note and [How it works](docs/how-it-works.md). |
| `.github/workflows/` | Continuous integration and release workflows. |
| `AGENTS.md`, `.codex/` | Project guidance and focused development procedures. |

## Contributing

Bug reports and change proposals are welcome through GitHub Issues and Pull Requests. See [CONTRIBUTING.md](CONTRIBUTING.md) for local checks and device compatibility notes.

## License

This project is available under the [MIT License](LICENSE). You may use, modify, distribute and sell the software, provided that the copyright and license notices are retained.