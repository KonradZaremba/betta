# Token benchmark report

- Date: 2026-11-13 20:15
- Model requested: opus (models seen in results: claude-haiku-5-5;claude-opus-5-5)
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
| u1-facade | betta-guide-skill-mcp | 3 | 360,761 (350,798-520,436) | 11,255 (10,687-13,168) | 0.551 (0.494-0.592) | 17 (12-17) | 117.0 (113.9-137.6) | 58,428 (56,973-62,923) | 3/3 | 4,742 (4,290-5,238) | n/a |
| u1-facade | vanilla | 3 | 660,743 (522,476-696,846) | 25,562 (24,788-26,258) | 0.915 (0.893-0.947) | 24 (18-31) | 245.6 (243.2-257.7) | 66,668 (65,902-67,168) | 3/3 | 10,890 (9,348-11,281) | n/a |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide-skill-mcp | 360,761 | 11,255 | 0.551 | 17 | 117.0 | 4,742 |
| vanilla | 660,743 | 25,562 | 0.915 | 24 | 245.6 | 10,890 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| u1-facade | betta-guide-skill-mcp | 0.55x | 0.44x | 0.60x | 0.71x | 0.44x |
| ALL | betta-guide-skill-mcp | 0.55x | 0.44x | 0.60x | 0.71x | 0.44x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

