# Collecting a compatibility report

MayaX-Battery remains a battery monitor for the LAMZU Maya X. Its diagnostics are a general, passive evidence-gathering feature; they do not add support for another mouse. To investigate another model, collect a report and use the companion prompt in a separate fork of the project.

## Automatic command-line export

Run the executable with exactly one argument, `--diagnostics`. It scans HID descriptors without opening a window or tray icon, then saves a uniquely named ZIP beside the executable. The output directory is based on the executable's actual path, not the current PowerShell directory; the application base directory is used only if the process path is unavailable. This mode does not start battery polling, migrate or write settings/history, or send vendor commands. It can run while the ordinary app is open, and its event log is normally empty because it is a separate process.

```powershell
$process = Start-Process -FilePath ".\MayaX-Battery.exe" -ArgumentList "--diagnostics" -Wait -PassThru
"Diagnostics exit code: $($process.ExitCode)"
```

Use `Start-Process -Wait -PassThru` to wait and read the process exit code reliably. Exit code `0` means the ZIP was saved, even if Windows returned an incomplete HID scan; read `summary.txt` and `devices.json` to see scan failures. Exit code `1` means capture or export failed. The program also tries to write a short `MayaX-diagnostics-error-<UTC timestamp>-<id>.txt` beside the executable. Exit code `2` means `--diagnostics` was combined with extra arguments. If saving fails, no ZIP is reported as successful and the program does not retry in another directory or request elevation. Check that the application folder allows writes, then run the command again. The generated ZIP name has a UTC timestamp and unique identifier, so repeated runs do not overwrite reports.

## Interactive alternatives

In an ordinary run, **Export diagnostics...** in the details window or tray menu opens a save dialog. The exported event log can include recent polling attempts from that process. If normal polling is stuck, launch a separate passive UI instance:

```powershell
.\MayaX-Battery.exe --diagnostics-only
```

This interactive mode disables battery polling and can coexist with the ordinary instance. Use its window or tray menu to choose where to save the report. Its event log is expected to be empty; it cannot read another process's in-memory events. Close the tray instance with **Exit** when finished.

## What the report contains and omits

The ZIP contains `summary.txt`, `devices.json` and `events.jsonl`. Discovery is intentionally scoped to interfaces with the current known vendor IDs `373E` and `3554`, or product/manufacturer names containing LAMZU or Maya. It is not a dump of every HID device on the computer. The report includes relevant IDs, HID usage and report lengths, whether capabilities were available, the reason each candidate matched, discovery errors, and any events logged by this process. Device paths and serial numbers are excluded; interface IDs are anonymous and only relate events to candidates within a report.

Firmware versions remain unknown unless obtained separately from a trusted official application. A USB descriptor release value is not firmware evidence. Report IDs are not collected. Diagnostic capture does not send mouse protocol commands, read input events, change pairing/settings, or read/write firmware or EEPROM. Thus, a report can show which descriptors Windows exposes without identifying a new battery protocol or proving compatibility.

## Prepare a useful investigation package

1. Run automatic export once with the receiver connected and save the generated ZIP path. Record whether this was wireless or wired.
2. If convenient, physically disconnect the receiver, connect the mouse directly with a USB data cable, and run export again. You perform the physical reconnection. Restore the usual connection afterwards. Do not change pairing or firmware.
3. Record the exact mouse variant, receiver type (if known), Windows version, whether pointer movement works, and the official configuration software name/version. Include mouse and receiver firmware versions only if the official software already displays them, and say where each value came from.
4. Review the report before sharing it with the developer or an AI agent. Keep it local by default and do not post it publicly without reviewing the contents.
5. In the fork, give the agent the exact model facts, both reports when available, and the [universal agent prompt](friend-agent-prompt.md). The prompt asks the agent to verify official protocol evidence and test a proposed implementation in the fork. The first report may still be insufficient to safely implement a protocol.

A receiver's USB identity does not by itself identify the paired mouse. Unknown values and incomplete discovery must stay explicitly unknown. Never treat a missing candidate as proof that a device has no firmware or HID reports.