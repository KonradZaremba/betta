# Context-cost calibration — 2026-10-20

What the Betta agent tooling adds to every Claude Code session, measured with real sessions rather than estimated.

Each row is a headless `claude -p` session on Claude Sonnet 5.5 with the benchmark's isolation flags (`--setting-sources project,local --strict-mcp-config`, no user skills, MCP servers or hooks). The isolated base session loads only Claude Code's built-in tools and skills.

"Context" is the size of the final API call (input + cache read + cache creation). The delta is measured against the base session.

| Session | Prompt | Turns | Context | Delta | Cost |
|---|---|---|---|---|---|
| `c1-base` — empty folder | Reply OK | 1 | 36,029 | — | $0.022 |
| `c2-skill-idle` — betta-plugin skill installed | Reply OK | 1 | 36,280 | **+251** | $0.031 |
| `c3-mcp-idle` — Betta.Mcp connected | Reply OK | 1 | 36,206 | **+177** | $0.031 |
| `c4-skill-loaded` — skill invoked | Invoke the skill, reply OK | 3 | 44,677 | **+8,648** | $0.062 |
| `c5-mcp-loaded` — all 4 tool schemas fetched | ToolSearch the betta tools, reply OK | 2 | 37,342 | **+1,313** | $0.034 |

## Reading it

- **Idle cost is small.** An installed skill costs only its listing line (~250 tokens). A connected MCP server costs only its tool names (~180 tokens), because Claude Code defers MCP schemas until a tool is needed.
- **Loading is where the cost is.** Invoking the skill puts its whole body in context: ~8.4k tokens on top of the idle cost, plus the tool call around it. Fetching all four Betta.Mcp schemas costs ~1.1k.
- **chars/4 underestimates.** The skill file is 21,480 characters (~5.4k tokens by chars/4), but it costs ~8.4k in practice. Treat the harness's `added_est_tokens` as a relative measure only.

Raw results: `c1-base.json` … `c5-mcp-loaded.json` in this folder.
