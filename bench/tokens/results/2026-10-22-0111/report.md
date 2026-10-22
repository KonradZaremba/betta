# Token benchmark report

- Date: 2026-10-22 01:40
- Model requested: sonnet (models seen in results: claude-haiku-5-5;claude-sonnet-5-5)
- Runs per task x arm: 3
- Max turns: 40; max budget per run: $3
- Arms: betta-guide, vanilla, betta-guide-mcp
- Repo commit (skill version): 987dacf
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- MCP arm: betta-guide-mcp: --mcp-config with one stdio server "betta" (Betta.Mcp from this checkout, BETTA_PLUGIN_FOLDER = the task's published build) and mcp__betta + ToolSearch added to --allowedTools
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens | GUID kept |
|---|---|---|---|---|---|---|---|---|---|---|---|
| t1-remap | betta-guide | 3 | 154,488 (154,310-154,496) | 1,423 (1,321-1,423) | 0.068 (0.067-0.068) | 6 (6-6) | 25.0 (19.6-32.1) | 39,200 (39,118-39,208) | 3/3 | 230 (230-230) | n/a |
| t1-remap | vanilla | 3 | 152,219 (151,841-152,286) | 1,682 (1,613-1,720) | 0.070 (0.069-0.070) | 7 (6-7) | 22.7 (20.5-23.3) | 38,951 (38,847-39,005) | 3/3 | 496 (486-498) | n/a |
| t2-divide | betta-guide | 3 | 154,868 (154,332-194,174) | 1,512 (1,283-1,754) | 0.070 (0.067-0.078) | 6 (5-6) | 18.0 (16.7-24.7) | 39,455 (39,151-39,683) | 3/3 | 303 (279-305) | n/a |
| t2-divide | vanilla | 3 | 151,839 (150,994-151,915) | 1,540 (1,461-1,597) | 0.068 (0.066-0.069) | 6 (5-6) | 22.7 (21.9-23.9) | 38,891 (38,503-38,898) | 3/3 | 455 (452-455) | n/a |
| t3-async | betta-guide | 3 | 154,677 (154,439-194,345) | 1,493 (1,397-1,721) | 0.068 (0.068-0.076) | 5 (5-7) | 25.8 (25.7-27.4) | 39,210 (39,083-39,554) | 3/3 | 373 (350-378) | n/a |
| t3-async | vanilla | 3 | 154,200 (153,753-194,552) | 2,664 (2,417-2,919) | 0.084 (0.081-0.092) | 6 (6-8) | 27.9 (24.0-51.0) | 39,997 (39,815-40,365) | 3/3 | 930 (926-947) | n/a |
| t4-multi | betta-guide | 3 | 198,640 (197,917-207,122) | 2,899 (2,490-4,762) | 0.096 (0.091-0.128) | 6 (6-6) | 25.4 (22.3-34.4) | 41,357 (41,144-44,748) | 3/3 | 1,117 (1,053-1,140) | n/a |
| t4-multi | vanilla | 3 | 257,050 (212,136-312,812) | 9,713 (9,074-10,323) | 0.189 (0.185-0.215) | 11 (11-14) | 62.2 (57.7-70.2) | 47,650 (47,098-50,081) | 3/3 | 2,455 (2,435-2,471) | n/a |
| t5-refactor | betta-guide | 3 | 194,696 (154,769-195,314) | 1,729 (1,563-1,832) | 0.079 (0.070-0.081) | 7 (5-7) | 23.5 (18.3-24.8) | 40,011 (39,206-40,238) | 3/3 | 183 (162-183) | 3/3 |
| t5-refactor | vanilla | 3 | 192,471 (155,008-238,565) | 2,100 (2,019-2,803) | 0.082 (0.080-0.101) | 11 (10-13) | 28.1 (22.5-28.4) | 40,353 (40,299-41,974) | 3/3 | 194 (190-194) | n/a |
| t5-refactor | betta-guide-mcp | 3 | 155,706 (155,670-155,901) | 1,544 (1,537-1,684) | 0.071 (0.071-0.073) | 5 (5-5) | 18.9 (17.4-19.0) | 39,556 (39,536-39,627) | 3/3 | 162 (162-208) | 3/3 |
| t6-shelfpack | betta-guide | 3 | 200,951 (200,711-207,160) | 3,578 (3,479-5,792) | 0.106 (0.105-0.136) | 7 (6-7) | 30.8 (30.6-39.7) | 42,293 (42,181-44,106) | 3/3 | 1,360 (1,155-1,384) | n/a |
| t6-shelfpack | vanilla | 3 | 205,148 (201,808-241,482) | 4,627 (4,592-6,540) | 0.122 (0.117-0.144) | 7 (6-7) | 43.4 (37.0-44.5) | 42,577 (42,300-44,238) | 3/3 | 1,840 (1,624-1,927) | n/a |
| t7-panels | betta-guide | 3 | 158,691 (158,569-202,689) | 2,618 (2,537-4,357) | 0.087 (0.086-0.115) | 6 (6-7) | 22.6 (22.3-31.4) | 40,735 (40,701-42,539) | 3/3 | 1,004 (955-1,019) | n/a |
| t7-panels | vanilla | 3 | 158,193 (156,758-197,529) | 3,416 (3,348-3,615) | 0.099 (0.094-0.100) | 6 (6-7) | 27.8 (27.0-30.0) | 40,974 (40,793-41,555) | 3/3 | 1,434 (1,432-1,553) | n/a |
| t8-rename | betta-guide | 3 | 234,939 (234,760-235,199) | 1,853 (1,770-2,012) | 0.086 (0.085-0.087) | 8 (8-8) | 19.8 (18.8-20.3) | 40,423 (40,384-40,520) | 3/3 | 77 (77-77) | 3/3 |
| t8-rename | vanilla | 3 | 194,111 (194,025-232,723) | 2,003 (1,568-2,047) | 0.082 (0.080-0.083) | 8 (8-9) | 21.4 (20.8-24.8) | 40,315 (39,855-40,329) | 3/3 | 97 (51-97) | n/a |
| t8-rename | betta-guide-mcp | 3 | 235,462 (235,424-235,881) | 1,666 (1,594-1,761) | 0.084 (0.083-0.085) | 8 (8-8) | 18.2 (17.9-19.5) | 40,496 (40,487-40,591) | 3/3 | 77 (76-77) | 3/3 |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide | 1,451,950 | 17,105 | 0.660 | 51 | 190.9 | 4,647 |
| vanilla | 1,465,231 | 27,745 | 0.797 | 62 | 256.4 | 7,901 |
| betta-guide-mcp | 391,168 | 3,210 | 0.155 | 13 | 37.1 | 239 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t1-remap | betta-guide | 1.01x | 0.85x | 0.98x | 0.86x | 0.46x |
| t1-remap | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t2-divide | betta-guide | 1.02x | 0.98x | 1.03x | 1.00x | 0.67x |
| t2-divide | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t3-async | betta-guide | 1.00x | 0.56x | 0.81x | 0.83x | 0.40x |
| t3-async | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t4-multi | betta-guide | 0.77x | 0.30x | 0.51x | 0.55x | 0.45x |
| t4-multi | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t5-refactor | betta-guide | 1.01x | 0.82x | 0.95x | 0.64x | 0.94x |
| t5-refactor | betta-guide-mcp | 0.81x | 0.74x | 0.86x | 0.45x | 0.84x |
| t6-shelfpack | betta-guide | 0.98x | 0.77x | 0.87x | 1.00x | 0.74x |
| t6-shelfpack | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t7-panels | betta-guide | 1.00x | 0.77x | 0.87x | 1.00x | 0.70x |
| t7-panels | betta-guide-mcp | n/a | n/a | n/a | n/a | n/a |
| t8-rename | betta-guide | 1.21x | 0.93x | 1.04x | 1.00x | 0.79x |
| t8-rename | betta-guide-mcp | 1.21x | 0.83x | 1.02x | 1.00x | 0.79x |
| ALL | betta-guide | 0.99x | 0.62x | 0.83x | 0.82x | 0.59x |
| ALL | betta-guide-mcp | 0.27x | 0.12x | 0.19x | 0.21x | 0.03x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

