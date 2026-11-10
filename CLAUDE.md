# MayaX-Battery - Claude Code

@AGENTS.md

## Claude Code layer

`AGENTS.md` above is the project guide for every coding assistant. This section adds what is specific to Claude Code. The Codex roles and configuration under `.codex/` do not apply to Claude; the two procedures under `.codex/skills/` do, through the entries below.

- This is a public repository. Keep maintained documentation and instructions in English, without em dashes. Never commit device dumps, diagnostic bundles, local paths, or credentials.
- Commits carry no AI co-author trailer and no mention of an assistant. Creating a release, pushing a tag, or rewriting history each need the owner's specific authorization.
- Build check: `dotnet build src/MayaX-Battery.csproj -c Release -v q "-clp:ErrorsOnly;NoSummary"`. The regression checks are a console project: `dotnet run --project tests/Checks.csproj -c Release`. `dotnet test` does not run them.
- Tests and previews must never contact HID devices or overwrite data under `%LOCALAPPDATA%/MayaX-Battery`. Do not pass `--device` unless hardware access is part of the request. A running copy of the app may be the owner's installed one: do not stop it.
- A screenshot proves layout, not hardware behavior. Say which is which.

### Skills (`.claude/skills`)

| Skill | Use for |
|---|---|
| `maya-ui-check` | UI text, language switching, settings, WPF layout, tray menu, preview screenshots |
| `maya-release-check` | CI, single-file ZIP packaging, release workflow verification |

Both are thin entries that point at the shared procedures in `.codex/skills/<name>/SKILL.md`.

### Agents (`.claude/agents`)

| Agent | Model | Use for |
|---|---|---|
| `maya-reviewer` | Opus 5.5 | Read-only review of a change against the behavior this app must preserve |

The project is small: implement and document directly, and use `maya-reviewer` once before a change to polling, HID access, runtime estimates, startup behavior, or storage is committed.