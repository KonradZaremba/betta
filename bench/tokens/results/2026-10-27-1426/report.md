# Token benchmark report

- Date: 2026-10-27 14:26
- Model requested: sonnet (models seen in results: claude-haiku-5-5;claude-sonnet-5-5)
- Runs per task x arm: 3
- Max turns: 60; max budget per run: $2
- Arms: betta-guide, vanilla
- Repo commit (skill version): e70ce01
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens | GUID kept |
|---|---|---|---|---|---|---|---|---|---|---|---|
| t9-iterate | betta-guide | 3 | 223,044 (222,415-226,661) | 4,250 (4,014-5,763) | 0.141 (0.139-0.169) | 15 (14-16) | 47.1 (38.6-113.6) | 48,943 (48,804-52,085) | 3/3 | 691 (678-703) | n/a |
| t9-iterate | vanilla | 3 | 313,967 (243,459-376,840) | 11,779 (8,609-12,766) | 0.286 (0.227-0.302) | 18 (15-21) | 81.6 (48.7-109.5) | 64,519 (59,330-64,562) | 3/3 | 1,284 (1,280-1,305) | n/a |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide | 223,044 | 4,250 | 0.141 | 15 | 47.1 | 691 |
| vanilla | 313,967 | 11,779 | 0.286 | 18 | 81.6 | 1,284 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t9-iterate | betta-guide | 0.71x | 0.36x | 0.49x | 0.83x | 0.54x |
| ALL | betta-guide | 0.71x | 0.36x | 0.49x | 0.83x | 0.54x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

