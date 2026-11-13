# Token benchmark report

- Date: 2026-11-13 19:39
- Model requested: haiku (models seen in results: claude-haiku-5-5)
- Runs per task x arm: 3
- Max turns: 80; max budget per run: $8
- Arms: betta-guide-skill-mcp, vanilla
- Repo commit (skill version): 00763cf
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- MCP arm: arms ending in -mcp: --mcp-config with one stdio server "betta" (Betta.Mcp from this checkout, BETTA_PLUGIN_FOLDER = the task's published build) and mcp__betta + ToolSearch added to --allowedTools
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens | GUID kept |
|---|---|---|---|---|---|---|---|---|---|---|---|
| u1-facade | betta-guide-skill-mcp | 3 | 1,102,425 (728,643-1,321,237) | 37,192 (35,206-38,221) | 0.040 (0.037-0.043) | 27 (15-31) | 182.0 (169.9-187.4) | 89,181 (86,444-89,568) | 3/3 | 5,706 (5,671-6,324) | n/a |
| u1-facade | vanilla | 3 | 1,102,875 (1,075,035-1,767,629) | 60,264 (59,190-62,970) | 0.093 (0.079-0.115) | 35 (35-38) | 270.2 (268.5-309.9) | 107,140 (106,758-113,075) | 3/3 | 9,995 (9,767-10,217) | n/a |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide-skill-mcp | 1,102,425 | 37,192 | 0.040 | 27 | 182.0 | 5,706 |
| vanilla | 1,102,875 | 60,264 | 0.093 | 35 | 270.2 | 9,995 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| u1-facade | betta-guide-skill-mcp | 1.00x | 0.62x | 0.43x | 0.77x | 0.57x |
| ALL | betta-guide-skill-mcp | 1.00x | 0.62x | 0.43x | 0.77x | 0.57x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

