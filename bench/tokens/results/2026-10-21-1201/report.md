# Token benchmark report

- Date: 2026-10-21 12:01
- Model requested: sonnet (models seen in results: claude-haiku-5-5;claude-sonnet-5-5)
- Runs per task x arm: 6
- Max turns: 40; max budget per run: $3
- Arms: betta-guide-mcp
- Repo commit (skill version): ad3a908
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- MCP arm: betta-guide-mcp: --mcp-config with one stdio server "betta" (Betta.Mcp from this checkout, BETTA_PLUGIN_FOLDER = the task's published build) and mcp__betta + ToolSearch added to --allowedTools
- Merged rows: t5 vanilla rows copied from run 2026-10-21-0811 and t5 betta-guide rows from run 2026-10-21-0926 (same model, day, scaffold and task; not re-run). Their guid/guid_kept were computed afterwards from the saved workspaces with the same check: betta_describe on the Release build, or Remap.cs's ComponentGuid for vanilla.
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens | GUID kept |
|---|---|---|---|---|---|---|---|---|---|---|---|
| t5-refactor | betta-guide-mcp | 6 | 216,103 (195,381-277,131) | 1,926 (1,810-2,208) | 0.086 (0.081-0.094) | 8 (7-10) | 35.6 (30.1-37.2) | 40,758 (40,265-41,171) | 6/6 | 201 (173-204) | 6/6 |
| t5-refactor | vanilla | 3 | 196,467 (194,773-196,972) | 2,851 (2,079-2,864) | 0.093 (0.083-0.097) | 11 (8-13) | 64.8 (48.2-66.1) | 41,298 (40,330-41,869) | 3/3 | 194 (190-197) | 3/3 |
| t5-refactor | betta-guide | 3 | 273,196 (194,661-356,143) | 2,281 (2,237-2,626) | 0.096 (0.082-0.110) | 9 (9-14) | 74.2 (43.2-107.6) | 41,055 (40,158-41,591) | 3/3 | 183 (162-190) | 0/3 |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide-mcp | 216,103 | 1,926 | 0.086 | 8 | 35.6 | 201 |
| vanilla | 196,467 | 2,851 | 0.093 | 11 | 64.8 | 194 |
| betta-guide | 273,196 | 2,281 | 0.096 | 9 | 74.2 | 183 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t5-refactor | betta-guide-mcp | 1.10x | 0.68x | 0.93x | 0.73x | 1.04x |
| t5-refactor | betta-guide | 1.39x | 0.80x | 1.03x | 0.82x | 0.94x |
| ALL | betta-guide-mcp | 1.10x | 0.68x | 0.93x | 0.73x | 1.04x |
| ALL | betta-guide | 1.39x | 0.80x | 1.03x | 0.82x | 0.94x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

