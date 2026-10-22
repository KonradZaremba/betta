# Token benchmark report

- Date: 2026-10-21 21:34
- Model requested: sonnet (models seen in results: claude-haiku-5-5;claude-sonnet-5-5)
- Runs per task x arm: 3
- Max turns: 40; max budget per run: $3
- Arms: betta-guide, betta-guide-mcp, vanilla
- Repo commit (skill version): 987dacf
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- MCP arm: betta-guide-mcp: --mcp-config with one stdio server "betta" (Betta.Mcp from this checkout, BETTA_PLUGIN_FOLDER = the task's published build) and mcp__betta + ToolSearch added to --allowedTools
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens | GUID kept |
|---|---|---|---|---|---|---|---|---|---|---|---|
| t1-remap | betta-guide | 3 | 153,992 (153,642-154,183) | 1,383 (1,222-1,474) | 0.067 (0.065-0.069) | 6 (5-6) | 22.4 (17.4-22.6) | 39,026 (38,828-39,132) | 3/3 | 230 (230-230) | n/a |
| t1-remap | vanilla | 3 | 151,876 (151,081-151,962) | 1,665 (1,585-1,714) | 0.069 (0.067-0.070) | 6 (5-6) | 70.3 (54.2-76.3) | 38,827 (38,479-38,873) | 3/3 | 510 (485-510) | n/a |
| t2-divide | betta-guide | 3 | 193,743 (153,990-232,984) | 1,440 (1,314-1,762) | 0.077 (0.067-0.078) | 6 (5-7) | 18.6 (14.4-35.8) | 39,631 (39,057-39,732) | 3/3 | 305 (303-323) | n/a |
| t2-divide | vanilla | 3 | 151,250 (150,937-151,829) | 1,523 (1,368-1,567) | 0.067 (0.065-0.068) | 5 (5-6) | 68.8 (58.6-119.8) | 38,659 (38,521-38,901) | 3/3 | 464 (455-509) | n/a |
| t3-async | betta-guide | 3 | 194,137 (193,714-194,369) | 1,618 (1,606-1,742) | 0.076 (0.074-0.076) | 7 (6-7) | 20.2 (14.7-39.8) | 39,587 (39,355-39,772) | 3/3 | 376 (373-379) | n/a |
| t3-async | vanilla | 3 | 153,655 (153,610-154,093) | 2,521 (2,441-2,664) | 0.079 (0.079-0.082) | 6 (6-6) | 70.4 (56.8-74.0) | 39,684 (39,565-39,890) | 3/3 | 904 (852-928) | n/a |
| t4-multi | betta-guide | 3 | 159,518 (159,265-160,368) | 2,775 (2,755-3,156) | 0.090 (0.090-0.096) | 6 (6-6) | 23.0 (21.1-34.0) | 41,228 (41,158-41,668) | 3/3 | 1,132 (1,114-1,141) | n/a |
| t4-multi | vanilla | 3 | 256,507 (255,831-256,921) | 8,553 (8,549-8,686) | 0.179 (0.179-0.181) | 12 (11-12) | 92.0 (91.0-112.0) | 47,013 (46,906-47,046) | 3/3 | 2,386 (2,320-2,396) | n/a |
| t5-refactor | betta-guide | 3 | 318,735 (195,205-372,427) | 2,631 (1,695-2,794) | 0.108 (0.079-0.129) | 12 (8-12) | 31.4 (31.3-40.2) | 42,202 (40,292-45,648) | 3/3 | 186 (183-190) | 0/3 |
| t5-refactor | betta-guide-mcp | 3 | 237,104 (235,157-237,541) | 2,018 (1,891-2,109) | 0.090 (0.089-0.093) | 9 (8-9) | 27.7 (22.8-30.9) | 41,333 (40,949-41,521) | 3/3 | 198 (179-198) | 3/3 |
| t5-refactor | vanilla | 3 | 196,811 (195,645-275,210) | 2,768 (2,666-2,787) | 0.096 (0.093-0.104) | 11 (10-13) | 140.1 (67.6-153.9) | 41,791 (41,290-41,824) | 3/3 | 190 (190-191) | n/a |
| t6-shelfpack | betta-guide | 3 | 241,272 (160,894-252,836) | 3,768 (3,573-6,254) | 0.113 (0.100-0.158) | 8 (6-8) | 44.3 (39.2-49.3) | 42,575 (41,719-47,424) | 3/3 | 1,422 (1,418-1,534) | n/a |
| t6-shelfpack | vanilla | 3 | 159,215 (158,858-159,858) | 4,128 (4,051-4,492) | 0.106 (0.104-0.111) | 6 (6-6) | 114.9 (83.5-124.8) | 41,910 (41,630-42,193) | 3/3 | 1,771 (1,746-1,917) | n/a |
| t7-panels | betta-guide | 3 | 198,221 (198,170-364,631) | 2,853 (2,850-3,655) | 0.095 (0.095-0.131) | 7 (7-12) | 30.5 (28.2-37.6) | 41,390 (41,325-44,091) | 3/3 | 1,036 (1,015-1,052) | n/a |
| t7-panels | vanilla | 3 | 157,126 (156,828-157,697) | 3,326 (3,308-3,599) | 0.094 (0.094-0.098) | 7 (6-7) | 65.2 (62.8-75.8) | 40,998 (40,888-41,290) | 3/3 | 1,412 (1,374-1,582) | n/a |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide | 1,459,618 | 16,468 | 0.628 | 52 | 190.4 | 4,687 |
| betta-guide-mcp | 237,104 | 2,018 | 0.090 | 9 | 27.7 | 198 |
| vanilla | 1,226,440 | 24,484 | 0.692 | 53 | 621.7 | 7,637 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t1-remap | betta-guide | 1.01x | 0.83x | 0.97x | 1.00x | 0.45x |
| t1-remap | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t2-divide | betta-guide | 1.28x | 0.95x | 1.15x | 1.20x | 0.66x |
| t2-divide | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t3-async | betta-guide | 1.26x | 0.64x | 0.96x | 1.17x | 0.42x |
| t3-async | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t4-multi | betta-guide | 0.62x | 0.32x | 0.50x | 0.50x | 0.47x |
| t4-multi | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t5-refactor | betta-guide | 1.62x | 0.95x | 1.13x | 1.09x | 0.98x |
| t5-refactor | betta-guide-mcp | 1.20x | 0.73x | 0.93x | 0.82x | 1.04x |
| t6-shelfpack | betta-guide | 1.52x | 0.91x | 1.07x | 1.33x | 0.80x |
| t6-shelfpack | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t7-panels | betta-guide | 1.26x | 0.86x | 1.01x | 1.00x | 0.73x |
| t7-panels | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| ALL | betta-guide | 1.19x | 0.67x | 0.91x | 0.98x | 0.61x |
| ALL | betta-guide-mcp | 0.19x | 0.08x | 0.13x | 0.17x | 0.03x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

