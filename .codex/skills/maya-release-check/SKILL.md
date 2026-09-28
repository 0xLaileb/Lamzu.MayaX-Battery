---
name: maya-release-check
description: Validate MayaX-Battery Windows CI, single-file ZIP contents and tag-triggered draft releases without authorizing publication.
---

# MayaX-Battery release checks

Read `global.json`, both project files, `tools/Build-Release.ps1` and the affected workflow. Use the existing console runner; `dotnet test` does not execute these checks.

1. Build Release and run hardware-independent checks on Windows. Avoid `--device` unless actual hardware access is part of the request.
2. Run `./tools/Build-Release.ps1 -Version 0.0.0-dev` for a local package check. Inspect the ZIP for `MayaX-Battery.exe` and the MIT license. Verify its SHA-256 against `SHA256SUMS.txt`.
3. Confirm the published executable completes the preview smoke check without HID access or changes to user settings. Report native launch failures even if compilation passed.
4. Validate edited YAML with `actionlint` when available. Keep build permissions read-only, and limit release write permission to the draft-release job. Pass tag values through environment variables and validate package versions.
5. Check the final diff for local paths, device dumps, credentials, `bin`, `obj` and `artifacts`. Do not upload local diagnostic bundles.

A local package is not a GitHub release. The release workflow runs after an authorized version tag and creates a draft; publishing that draft is a separate action. Never create a tag, publish a release or rewrite history merely to test the workflow. After an authorized push, verify the Actions run for the exact commit and report its actual result.