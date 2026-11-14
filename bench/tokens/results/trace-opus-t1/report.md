# Token benchmark report

- Date: 2026-11-14 11:34
- Model requested: opus (models seen in results: claude-haiku-5-5;claude-opus-5-5)
- Runs per task x arm: 3
- Max turns: 40; max budget per run: $3
- Arms: betta-guide, betta-guide-mcp
- Repo commit (skill version): 07f34d2
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- MCP arm: arms ending in -mcp: --mcp-config with one stdio server "betta" (Betta.Mcp from this checkout, BETTA_PLUGIN_FOLDER = the task's published build) and mcp__betta + ToolSearch added to --allowedTools
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens | GUID kept |
|---|---|---|---|---|---|---|---|---|---|---|---|
| t1-remap | betta-guide | 3 | 345,756 (335,030-359,317) | 2,322 (2,180-2,369) | 0.270 (0.212-0.281) | 10 (10-11) | 42.4 (38.9-44.1) | 50,243 (41,777-50,933) | 3/3 | 234 (234-247) | n/a |
| t1-remap | betta-guide-mcp | 3 | 278,735 (239,611-364,299) | 2,048 (1,794-2,615) | 0.187 (0.174-0.231) | 9 (8-12) | 33.7 (31.0-62.7) | 41,361 (41,319-43,297) | 3/3 | 238 (234-247) | n/a |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide | 345,756 | 2,322 | 0.270 | 10 | 42.4 | 234 |
| betta-guide-mcp | 278,735 | 2,048 | 0.187 | 9 | 33.7 | 238 |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

