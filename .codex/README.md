# Project assistance

`../AGENTS.md` contains the project map, behavioral constraints and verification commands. Start there in any coding assistant.

`agents/` defines three optional Codex roles: `maya_implementation`, `maya_reviewer` and `maya_docs`. Codex reads project configuration for trusted projects; this repository does not change trust, credentials, tool servers or the user's main model and effort settings. Start a new session if an existing session does not show newly added roles.

`skills/` contains focused UI and release procedures. They are linked explicitly from `AGENTS.md` so an assistant can open them as needed. They are not advertised as automatically discovered skills: the standard automatic repository discovery location is `.agents/skills`, while this project keeps procedures in the requested `.codex/skills` directory. No global installation is required to read and follow them.

References: [custom agents](https://learn.chatgpt.com/docs/agent-configuration/subagents), [project configuration](https://learn.chatgpt.com/docs/config-file/config-reference), [skill discovery](https://learn.chatgpt.com/docs/build-skills).