# Token benchmark report

- Date: 2026-10-21 12:24
- Model requested: sonnet (models seen in results: claude-haiku-5-5;claude-sonnet-5-5)
- Runs per task x arm: 3
- Max turns: 40; max budget per run: $3
- Arms: betta-guide, vanilla
- Repo commit (skill version): ad3a908
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens | GUID kept |
|---|---|---|---|---|---|---|---|---|---|---|---|
| t6-shelfpack | betta-guide | 3 | 887,058 (751,890-917,685) | 6,771 (6,545-7,327) | 0.280 (0.229-0.283) | 22 (20-23) | 115.6 (76.7-140.5) | 58,773 (51,952-61,901) | 3/3 | 1,367 (1,363-1,434) | n/a |
| t6-shelfpack | vanilla | 3 | 200,900 (159,322-205,778) | 4,261 (4,192-6,722) | 0.112 (0.107-0.147) | 6 (6-7) | 40.7 (39.2-45.1) | 41,951 (41,871-44,548) | 3/3 | 1,815 (1,779-1,853) | n/a |
| t7-panels | betta-guide | 3 | 851,797 (689,107-902,130) | 5,941 (5,187-7,812) | 0.234 (0.199-0.265) | 21 (19-26) | 111.8 (71.9-118.2) | 52,211 (49,324-54,212) | 3/3 | 964 (944-995) | n/a |
| t7-panels | vanilla | 3 | 156,967 (156,684-157,537) | 3,360 (3,199-3,580) | 0.095 (0.093-0.098) | 6 (6-7) | 35.7 (32.1-36.5) | 40,993 (40,874-41,149) | 3/3 | 1,446 (1,377-1,475) | n/a |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide | 1,738,855 | 12,712 | 0.513 | 43 | 227.4 | 2,331 |
| vanilla | 357,867 | 7,621 | 0.206 | 12 | 76.4 | 3,261 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t6-shelfpack | betta-guide | 4.42x | 1.59x | 2.51x | 3.67x | 0.75x |
| t7-panels | betta-guide | 5.43x | 1.77x | 2.46x | 3.50x | 0.67x |
| ALL | betta-guide | 4.86x | 1.67x | 2.49x | 3.58x | 0.71x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

