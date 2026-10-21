# Token benchmark report

- Date: 2026-10-21 13:14
- Model requested: sonnet (models seen in results: claude-haiku-5-5;claude-sonnet-5-5)
- Runs per task x arm: 3
- Max turns: 40; max budget per run: $3
- Arms: betta-guide
- Repo commit (skill version): ad3a908
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- Merged rows: vanilla rows copied from run 2026-10-21-1224 (same model, day, scaffold and tasks; not re-run)
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens | GUID kept |
|---|---|---|---|---|---|---|---|---|---|---|---|
| t6-shelfpack | betta-guide | 3 | 244,497 (200,976-330,800) | 4,445 (3,798-4,527) | 0.123 (0.109-0.134) | 8 (7-10) | 49.2 (43.6-54.7) | 43,218 (42,487-43,673) | 3/3 | 1,407 (1,302-1,444) | n/a |
| t6-shelfpack | vanilla | 3 | 200,900 (159,322-205,778) | 4,261 (4,192-6,722) | 0.112 (0.107-0.147) | 6 (6-7) | 40.7 (39.2-45.1) | 41,951 (41,871-44,548) | 3/3 | 1,815 (1,779-1,853) | n/a |
| t7-panels | betta-guide | 3 | 198,304 (158,101-198,790) | 2,938 (2,596-3,199) | 0.096 (0.086-0.100) | 7 (5-7) | 42.0 (34.0-46.1) | 41,402 (40,632-41,596) | 3/3 | 1,117 (1,050-1,128) | n/a |
| t7-panels | vanilla | 3 | 156,967 (156,684-157,537) | 3,360 (3,199-3,580) | 0.095 (0.093-0.098) | 6 (6-7) | 35.7 (32.1-36.5) | 40,993 (40,874-41,149) | 3/3 | 1,446 (1,377-1,475) | n/a |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide | 442,801 | 7,383 | 0.219 | 15 | 91.2 | 2,524 |
| vanilla | 357,867 | 7,621 | 0.206 | 12 | 76.4 | 3,261 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t6-shelfpack | betta-guide | 1.22x | 1.04x | 1.10x | 1.33x | 0.78x |
| t7-panels | betta-guide | 1.26x | 0.87x | 1.02x | 1.17x | 0.77x |
| ALL | betta-guide | 1.24x | 0.97x | 1.06x | 1.25x | 0.77x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

