# Token benchmark report

- Date: 2026-11-13 20:04
- Model requested: sonnet (models seen in results: claude-haiku-5-5;claude-sonnet-5-5)
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
| u1-facade | betta-guide-skill-mcp | 3 | 302,102 (192,601-323,585) | 9,005 (8,175-14,933) | 0.224 (0.193-0.304) | 12 (10-13) | 63.4 (55.1-86.3) | 56,458 (53,632-61,363) | 3/3 | 3,696 (3,629-3,861) | n/a |
| u1-facade | vanilla | 3 | 409,020 (300,815-456,614) | 20,162 (19,370-28,348) | 0.355 (0.349-0.478) | 14 (10-21) | 134.7 (119.5-163.2) | 60,229 (59,914-69,947) | 3/3 | 6,817 (6,600-7,560) | n/a |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide-skill-mcp | 302,102 | 9,005 | 0.224 | 12 | 63.4 | 3,696 |
| vanilla | 409,020 | 20,162 | 0.355 | 14 | 134.7 | 6,817 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| u1-facade | betta-guide-skill-mcp | 0.74x | 0.45x | 0.63x | 0.86x | 0.54x |
| ALL | betta-guide-skill-mcp | 0.74x | 0.45x | 0.63x | 0.86x | 0.54x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

