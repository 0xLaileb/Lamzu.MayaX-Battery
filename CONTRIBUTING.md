# Contributing

Bug reports and suggestions are welcome through GitHub Issues. For code changes, create a branch and open a pull request with a short description of the change and its purpose.

## Local checks

Use Windows with .NET SDK 10.0.401, as specified in `global.json`. From the repository root, run these commands in PowerShell:

```powershell
dotnet build src/MayaX-Battery.csproj -c Release
dotnet run --project tests/Checks.csproj -c Release
```

The checks run as a console program. This project does not use `dotnet test`. They must not contact HID devices, change the startup entry or touch data under `%LOCALAPPDATA%\MayaX-Battery`; use disposable files under the temp directory.

To package and smoke-check the single-file executable:

```powershell
.\tools\Build-Release.ps1 -Version 0.0.0-dev
```

## Refreshing screenshots

To regenerate the English screenshots in `docs/`, run:

```powershell
.\tools\Update-Previews.ps1
```

The script builds the app and renders demonstration data for the tray icon, details window, and context menu in English. The tracked screenshots in `docs/` are also in English.

To inspect another state or language, call the preview modes directly and write to the ignored `artifacts/` folder:

```powershell
$exe = "src\bin\Release\net10.0-windows\MayaX-Battery.exe"
& $exe --ui-preview artifacts\low-ru.png low 2 --language ru
& $exe --menu-preview artifacts\menu-ru.png submenu --language ru
```

Window states are `normal`, `learning`, `charging`, `low`, `critical`, `full`, `busy` and `error`. The number is the render scale from 1 to 2, and `narrow` renders a narrower window.

## User-facing text

Every string shown to the user lives in `src/Localization.cs` and needs both an English and a Russian value. Device names and failure codes stay the same in every language.

## Device and protocol changes

The project targets the LAMZU Maya X. For changes involving HID or device compatibility, include the device model and explain how the behavior was checked. Mark unverified compatibility as unverified. Remove personal paths, serial numbers, and other private information from logs and screenshots before posting them.