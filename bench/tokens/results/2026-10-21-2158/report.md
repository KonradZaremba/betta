# Token benchmark report

- Date: 2026-10-21 22:08
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
| t1-remap | betta-guide | 3 | 153,787 (153,755-154,333) | 1,241 (1,217-1,476) | 0.065 (0.065-0.069) | 5 (5-6) | 19.5 (12.6-22.5) | 38,852 (38,844-39,151) | 3/3 | 230 (230-230) | n/a |
| t1-remap | vanilla | 3 | 151,876 (151,081-151,962) | 1,665 (1,585-1,714) | 0.069 (0.067-0.070) | 6 (5-6) | 70.3 (54.2-76.3) | 38,827 (38,479-38,873) | 3/3 | 510 (485-510) | n/a |
| t2-divide | betta-guide | 3 | 154,144 (154,130-192,880) | 1,305 (1,294-1,310) | 0.067 (0.067-0.072) | 5 (5-6) | 15.3 (14.3-19.0) | 39,101 (39,089-39,381) | 3/3 | 303 (303-303) | n/a |
| t2-divide | vanilla | 3 | 151,250 (150,937-151,829) | 1,523 (1,368-1,567) | 0.067 (0.065-0.068) | 5 (5-6) | 68.8 (58.6-119.8) | 38,659 (38,521-38,901) | 3/3 | 464 (455-509) | n/a |
| t3-async | betta-guide | 3 | 154,202 (154,177-154,859) | 1,362 (1,352-1,619) | 0.067 (0.067-0.071) | 5 (5-6) | 18.9 (18.7-28.1) | 39,047 (39,006-39,437) | 3/3 | 378 (374-380) | n/a |
| t3-async | vanilla | 3 | 153,655 (153,610-154,093) | 2,521 (2,441-2,664) | 0.079 (0.079-0.082) | 6 (6-6) | 70.4 (56.8-74.0) | 39,684 (39,565-39,890) | 3/3 | 904 (852-928) | n/a |
| t4-multi | betta-guide | 3 | 158,550 (158,304-158,573) | 2,700 (2,618-2,708) | 0.088 (0.086-0.088) | 6 (5-6) | 30.4 (24.7-37.4) | 40,765 (40,597-40,780) | 3/3 | 1,124 (1,013-1,152) | n/a |
| t4-multi | vanilla | 3 | 256,507 (255,831-256,921) | 8,553 (8,549-8,686) | 0.179 (0.179-0.181) | 12 (11-12) | 92.0 (91.0-112.0) | 47,013 (46,906-47,046) | 3/3 | 2,386 (2,320-2,396) | n/a |
| t5-refactor | betta-guide | 3 | 154,519 (154,472-154,609) | 1,526 (1,472-1,534) | 0.069 (0.069-0.070) | 5 (5-5) | 16.9 (15.0-27.6) | 39,212 (39,113-39,243) | 3/3 | 162 (158-165) | 3/3 |
| t5-refactor | betta-guide-mcp | 3 | 155,500 (155,495-155,551) | 1,606 (1,557-1,617) | 0.071 (0.070-0.071) | 5 (5-5) | 17.6 (17.5-22.8) | 39,502 (39,450-39,503) | 3/3 | 162 (158-183) | 3/3 |
| t5-refactor | vanilla | 3 | 196,811 (195,645-275,210) | 2,768 (2,666-2,787) | 0.096 (0.093-0.104) | 11 (10-13) | 140.1 (67.6-153.9) | 41,791 (41,290-41,824) | 3/3 | 190 (190-191) | n/a |
| t6-shelfpack | betta-guide | 3 | 200,220 (160,524-201,714) | 3,331 (3,266-4,132) | 0.103 (0.096-0.114) | 7 (6-7) | 31.6 (25.9-33.0) | 42,026 (41,524-42,721) | 3/3 | 1,327 (1,289-1,478) | n/a |
| t6-shelfpack | vanilla | 3 | 159,215 (158,858-159,858) | 4,128 (4,051-4,492) | 0.106 (0.104-0.111) | 6 (6-6) | 114.9 (83.5-124.8) | 41,910 (41,630-42,193) | 3/3 | 1,771 (1,746-1,917) | n/a |
| t7-panels | betta-guide | 3 | 158,922 (158,514-158,942) | 2,818 (2,603-2,839) | 0.089 (0.086-0.090) | 6 (6-6) | 27.6 (24.4-28.9) | 40,928 (40,736-40,929) | 3/3 | 1,088 (999-1,120) | n/a |
| t7-panels | vanilla | 3 | 157,126 (156,828-157,697) | 3,326 (3,308-3,599) | 0.094 (0.094-0.098) | 7 (6-7) | 65.2 (62.8-75.8) | 40,998 (40,888-41,290) | 3/3 | 1,412 (1,374-1,582) | n/a |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide | 1,134,344 | 14,283 | 0.548 | 39 | 160.2 | 4,612 |
| betta-guide-mcp | 155,500 | 1,606 | 0.071 | 5 | 17.6 | 162 |
| vanilla | 1,226,440 | 24,484 | 0.692 | 53 | 621.7 | 7,637 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t1-remap | betta-guide | 1.01x | 0.75x | 0.94x | 0.83x | 0.45x |
| t1-remap | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t2-divide | betta-guide | 1.02x | 0.86x | 0.99x | 1.00x | 0.65x |
| t2-divide | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t3-async | betta-guide | 1.00x | 0.54x | 0.85x | 0.83x | 0.42x |
| t3-async | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t4-multi | betta-guide | 0.62x | 0.32x | 0.49x | 0.50x | 0.47x |
| t4-multi | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t5-refactor | betta-guide | 0.79x | 0.55x | 0.72x | 0.45x | 0.85x |
| t5-refactor | betta-guide-mcp | 0.79x | 0.58x | 0.74x | 0.45x | 0.85x |
| t6-shelfpack | betta-guide | 1.26x | 0.81x | 0.97x | 1.17x | 0.75x |
| t6-shelfpack | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t7-panels | betta-guide | 1.01x | 0.85x | 0.95x | 0.86x | 0.77x |
| t7-panels | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| ALL | betta-guide | 0.92x | 0.58x | 0.79x | 0.74x | 0.60x |
| ALL | betta-guide-mcp | 0.13x | 0.07x | 0.10x | 0.09x | 0.02x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

