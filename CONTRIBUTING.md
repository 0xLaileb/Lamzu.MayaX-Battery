# Contributing

Bug reports and suggestions are welcome through GitHub Issues. For code changes, create a branch and open a pull request with a short description of the change and its purpose.

## Local checks

Use Windows with .NET SDK 10.0.401, as specified in `global.json`. From the repository root, run these commands in PowerShell:

```powershell
dotnet build src/MayaX-Battery.csproj -c Release
dotnet run --project tests/Checks.csproj -c Release
```

The checks run as a console program. This project does not use `dotnet test`.

## Refreshing screenshots

To regenerate the English screenshots in `docs/`, run:

```powershell
.\tools\Update-Previews.ps1
```

The script builds the app and renders demonstration data for the tray icon, details window, and context menu in English. The tracked screenshots in `docs/` are also in English.

## Device and protocol changes

The project targets the LAMZU Maya X. For changes involving HID or device compatibility, include the device model and explain how the behavior was checked. Mark unverified compatibility as unverified. Remove personal paths, serial numbers, and other private information from logs and screenshots before posting them.