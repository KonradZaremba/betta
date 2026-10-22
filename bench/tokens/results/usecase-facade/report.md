# Token benchmark report

- Date: 2026-10-22 11:14
- Model requested: sonnet (models seen in results: claude-haiku-5-5;claude-sonnet-5-5)
- Runs per task x arm: 3
- Max turns: 80; max budget per run: $8
- Arms: betta-guide-skill-mcp, vanilla, betta-guide
- Repo commit (skill version): 987dacf
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- MCP arm: arms ending in -mcp: --mcp-config with one stdio server "betta" (Betta.Mcp from this checkout, BETTA_PLUGIN_FOLDER = the task's published build) and mcp__betta + ToolSearch added to --allowedTools
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens | GUID kept |
|---|---|---|---|---|---|---|---|---|---|---|---|
| u1-facade | betta-guide-skill-mcp | 3 | 248,566 (191,648-254,552) | 9,773 (8,699-13,754) | 0.223 (0.201-0.277) | 12 (11-13) | 90.3 (69.4-126.8) | 55,329 (53,838-58,942) | 3/3 | 3,780 (3,575-3,809) | n/a |
| u1-facade | vanilla | 3 | 380,537 (322,353-396,142) | 24,643 (17,902-25,788) | 0.413 (0.332-0.434) | 13 (10-16) | 178.4 (160.4-182.0) | 64,413 (58,895-65,657) | 3/3 | 6,852 (6,327-6,873) | n/a |
| u1-facade | betta-guide | 3 | 378,785 (365,830-394,874) | 10,169 (9,358-15,775) | 0.276 (0.228-0.318) | 13 (12-14) | 88.4 (86.7-118.5) | 61,099 (54,486-64,091) | 3/3 | 3,944 (3,749-4,144) | n/a |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide-skill-mcp | 248,566 | 9,773 | 0.223 | 12 | 90.3 | 3,780 |
| vanilla | 380,537 | 24,643 | 0.413 | 13 | 178.4 | 6,852 |
| betta-guide | 378,785 | 10,169 | 0.276 | 13 | 88.4 | 3,944 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| u1-facade | betta-guide-skill-mcp | 0.65x | 0.40x | 0.54x | 0.92x | 0.55x |
| u1-facade | betta-guide | 1.00x | 0.41x | 0.67x | 1.00x | 0.58x |
| ALL | betta-guide-skill-mcp | 0.65x | 0.40x | 0.54x | 0.92x | 0.55x |
| ALL | betta-guide | 1.00x | 0.41x | 0.67x | 1.00x | 0.58x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

