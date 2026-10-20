# AGENTS.md

Guidance for Codex and other agents that read AGENTS.md.

This repo's agent guidance is kept in one place so it cannot drift. Read these before changing anything:

- [CLAUDE.md](CLAUDE.md) — project overview, solution layout, build/test commands, architecture, rules and gotchas.
- [TestBetta/CLAUDE.md](TestBetta/CLAUDE.md) — the 6 expected test failures, the `Requires=Rhino` trait CI filters on, and headless assembly resolution.

Task-specific instructions live in `.claude/skills/`:

- [betta-plugin](.claude/skills/betta-plugin/SKILL.md) — writing a Betta plugin (attributes, return-type mapping, GUID stability, tooling).
- [betta-release](.claude/skills/betta-release/SKILL.md) — version bumps, NuGet/Yak packaging, the `v*` tag release workflow.
