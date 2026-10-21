# Token benchmark report

- Date: 2026-10-21 16:20
- Model requested: sonnet (models seen in results: claude-haiku-5-5;claude-sonnet-5-5)
- Runs per task x arm: 3
- Max turns: 40; max budget per run: $3
- Arms: betta-guide, vanilla, betta-guide-mcp
- Repo commit (skill version): ad3a908
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- MCP arm: betta-guide-mcp: --mcp-config with one stdio server "betta" (Betta.Mcp from this checkout, BETTA_PLUGIN_FOLDER = the task's published build) and mcp__betta + ToolSearch added to --allowedTools
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens | GUID kept |
|---|---|---|---|---|---|---|---|---|---|---|---|
| t1-remap | betta-guide | 3 | 154,637 (154,498-154,642) | 1,532 (1,431-1,544) | 0.070 (0.068-0.070) | 6 (6-6) | 76.5 (46.4-96.4) | 39,230 (39,187-39,247) | 3/3 | 230 (230-236) | n/a |
| t1-remap | vanilla | 3 | 151,876 (151,081-151,962) | 1,665 (1,585-1,714) | 0.069 (0.067-0.070) | 6 (5-6) | 70.3 (54.2-76.3) | 38,827 (38,479-38,873) | 3/3 | 510 (485-510) | n/a |
| t2-divide | betta-guide | 3 | 313,652 (235,999-317,532) | 2,423 (2,296-2,500) | 0.100 (0.091-0.103) | 9 (7-9) | 139.8 (68.4-161.1) | 40,677 (40,590-41,169) | 3/3 | 315 (313-317) | n/a |
| t2-divide | vanilla | 3 | 151,250 (150,937-151,829) | 1,523 (1,368-1,567) | 0.067 (0.065-0.068) | 5 (5-6) | 68.8 (58.6-119.8) | 38,659 (38,521-38,901) | 3/3 | 464 (455-509) | n/a |
| t3-async | betta-guide | 3 | 274,910 (237,531-313,235) | 2,571 (2,367-2,776) | 0.098 (0.094-0.100) | 8 (8-9) | 65.4 (56.1-71.3) | 40,665 (40,335-41,029) | 3/3 | 389 (389-390) | n/a |
| t3-async | vanilla | 3 | 153,655 (153,610-154,093) | 2,521 (2,441-2,664) | 0.079 (0.079-0.082) | 6 (6-6) | 70.4 (56.8-74.0) | 39,684 (39,565-39,890) | 3/3 | 904 (852-928) | n/a |
| t4-multi | betta-guide | 3 | 282,780 (200,328-320,251) | 3,531 (3,321-3,564) | 0.114 (0.102-0.119) | 10 (7-10) | 108.0 (101.1-179.8) | 42,340 (41,853-42,524) | 3/3 | 1,170 (1,118-1,195) | n/a |
| t4-multi | vanilla | 3 | 256,507 (255,831-256,921) | 8,553 (8,549-8,686) | 0.179 (0.179-0.181) | 12 (11-12) | 92.0 (91.0-112.0) | 47,013 (46,906-47,046) | 3/3 | 2,386 (2,320-2,396) | n/a |
| t5-refactor | betta-guide | 3 | 356,929 (317,486-367,747) | 2,561 (2,538-2,814) | 0.111 (0.106-0.131) | 12 (11-12) | 117.8 (101.1-141.5) | 41,732 (41,715-46,963) | 3/3 | 163 (158-208) | 0/3 |
| t5-refactor | vanilla | 3 | 196,811 (195,645-275,210) | 2,768 (2,666-2,787) | 0.096 (0.093-0.104) | 11 (10-13) | 140.1 (67.6-153.9) | 41,791 (41,290-41,824) | 3/3 | 190 (190-191) | n/a |
| t5-refactor | betta-guide-mcp | 3 | 238,130 (235,556-285,720) | 2,068 (2,007-2,097) | 0.093 (0.087-0.111) | 9 (7-10) | 89.6 (59.7-89.8) | 41,637 (40,807-45,170) | 3/3 | 203 (200-204) | 3/3 |
| t6-shelfpack | betta-guide | 3 | 203,256 (201,510-204,239) | 3,789 (3,709-4,020) | 0.110 (0.107-0.111) | 6 (6-7) | 108.5 (87.2-170.8) | 42,319 (42,006-42,574) | 3/3 | 1,513 (1,398-1,553) | n/a |
| t6-shelfpack | vanilla | 3 | 159,215 (158,858-159,858) | 4,128 (4,051-4,492) | 0.106 (0.104-0.111) | 6 (6-6) | 114.9 (83.5-124.8) | 41,910 (41,630-42,193) | 3/3 | 1,771 (1,746-1,917) | n/a |
| t7-panels | betta-guide | 3 | 243,377 (159,571-281,592) | 3,378 (3,061-4,562) | 0.112 (0.093-0.124) | 8 (6-9) | 97.5 (78.0-98.8) | 42,142 (41,172-43,275) | 3/3 | 1,088 (1,048-1,123) | n/a |
| t7-panels | vanilla | 3 | 157,126 (156,828-157,697) | 3,326 (3,308-3,599) | 0.094 (0.094-0.098) | 7 (6-7) | 65.2 (62.8-75.8) | 40,998 (40,888-41,290) | 3/3 | 1,412 (1,374-1,582) | n/a |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide | 1,829,541 | 19,785 | 0.714 | 59 | 713.3 | 4,868 |
| vanilla | 1,226,440 | 24,484 | 0.692 | 53 | 621.7 | 7,637 |
| betta-guide-mcp | 238,130 | 2,068 | 0.093 | 9 | 89.6 | 203 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t1-remap | betta-guide | 1.02x | 0.92x | 1.01x | 1.00x | 0.45x |
| t1-remap | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t2-divide | betta-guide | 2.07x | 1.59x | 1.48x | 1.80x | 0.68x |
| t2-divide | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t3-async | betta-guide | 1.79x | 1.02x | 1.24x | 1.33x | 0.43x |
| t3-async | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t4-multi | betta-guide | 1.10x | 0.41x | 0.64x | 0.83x | 0.49x |
| t4-multi | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t5-refactor | betta-guide | 1.81x | 0.93x | 1.15x | 1.09x | 0.86x |
| t5-refactor | betta-guide-mcp | 1.21x | 0.75x | 0.96x | 0.82x | 1.07x |
| t6-shelfpack | betta-guide | 1.28x | 0.92x | 1.03x | 1.00x | 0.85x |
| t6-shelfpack | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t7-panels | betta-guide | 1.55x | 1.02x | 1.19x | 1.14x | 0.77x |
| t7-panels | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| ALL | betta-guide | 1.49x | 0.81x | 1.03x | 1.11x | 0.64x |
| ALL | betta-guide-mcp | 0.19x | 0.08x | 0.13x | 0.17x | 0.03x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

