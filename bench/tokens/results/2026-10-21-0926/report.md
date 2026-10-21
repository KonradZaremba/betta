# Token benchmark report

- Date: 2026-10-21 09:26
- Model requested: sonnet (models seen in results: claude-haiku-5-5;claude-sonnet-5-5)
- Runs per task x arm: 3
- Max turns: 40; max budget per run: $3
- Arms: betta, betta-guide, betta-guide-skill, betta-noskill, vanilla
- Repo commit (skill version): ad3a908
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens |
|---|---|---|---|---|---|---|---|---|---|---|
| t1-remap | betta | 3 | 158,922 (158,826-199,889) | 1,455 (1,415-1,463) | 0.076 (0.075-0.080) | 8 (8-9) | 44.8 (34.8-51.2) | 40,872 (40,833-40,961) | 3/3 | 223 (210-223) |
| t1-remap | betta-guide | 3 | 153,374 (153,208-153,410) | 1,414 (1,313-1,420) | 0.067 (0.066-0.067) | 6 (6-6) | 34.1 (31.4-50.4) | 38,876 (38,826-38,927) | 3/3 | 215 (211-215) |
| t1-remap | betta-guide-skill | 3 | 154,053 (153,619-154,228) | 1,359 (1,175-1,455) | 0.067 (0.064-0.068) | 6 (5-6) | 38.0 (30.0-58.7) | 39,058 (38,781-39,125) | 3/3 | 215 (211-215) |
| t1-remap | betta-noskill | 3 | 150,958 (150,758-151,352) | 1,360 (1,191-1,410) | 0.063 (0.062-0.066) | 6 (6-7) | 32.6 (28.8-35.4) | 38,304 (38,301-38,663) | 3/3 | 210 (210-215) |
| t1-remap | vanilla | 3 | 152,151 (151,895-152,210) | 1,667 (1,640-1,679) | 0.069 (0.069-0.070) | 7 (6-7) | 32.2 (31.1-32.7) | 38,924 (38,853-38,971) | 3/3 | 487 (475-488) |
| t2-divide | betta | 3 | 199,612 (159,957-199,821) | 1,435 (1,321-1,455) | 0.080 (0.078-0.082) | 8 (8-8) | 34.4 (33.5-67.9) | 41,351 (41,304-41,370) | 3/3 | 293 (265-293) |
| t2-divide | betta-guide | 3 | 153,147 (153,125-191,685) | 1,253 (1,251-1,260) | 0.065 (0.065-0.070) | 5 (5-6) | 33.2 (25.3-41.7) | 38,788 (38,758-39,093) | 3/3 | 272 (227-284) |
| t2-divide | betta-guide-skill | 3 | 153,930 (153,884-153,967) | 1,256 (1,235-1,272) | 0.066 (0.066-0.066) | 5 (5-5) | 28.0 (25.8-28.6) | 38,983 (38,962-39,030) | 3/3 | 271 (259-271) |
| t2-divide | betta-noskill | 3 | 151,210 (151,121-151,222) | 1,375 (1,330-1,382) | 0.065 (0.064-0.065) | 6 (6-6) | 23.6 (23.3-23.7) | 38,501 (38,476-38,520) | 3/3 | 289 (251-289) |
| t2-divide | vanilla | 3 | 151,860 (151,839-151,880) | 1,548 (1,546-1,558) | 0.068 (0.068-0.068) | 6 (6-6) | 26.3 (26.1-26.5) | 38,918 (38,894-38,927) | 3/3 | 482 (456-482) |
| t3-async | betta | 3 | 161,033 (160,909-161,051) | 1,511 (1,443-1,525) | 0.080 (0.079-0.081) | 8 (8-8) | 40.5 (31.2-42.4) | 41,886 (41,833-41,892) | 3/3 | 383 (372-383) |
| t3-async | betta-guide | 3 | 153,414 (153,384-153,563) | 1,371 (1,366-1,528) | 0.066 (0.066-0.067) | 5 (5-5) | 51.8 (34.4-77.3) | 38,869 (38,844-38,879) | 3/3 | 378 (376-396) |
| t3-async | betta-guide-skill | 3 | 154,332 (154,112-154,625) | 1,523 (1,321-1,546) | 0.067 (0.067-0.070) | 5 (5-6) | 27.7 (26.9-31.8) | 39,049 (39,047-39,337) | 3/3 | 372 (347-374) |
| t3-async | betta-noskill | 3 | 581,829 (543,114-630,517) | 3,980 (3,493-5,530) | 0.180 (0.180-0.189) | 16 (16-17) | 71.9 (67.2-89.2) | 50,339 (49,819-50,675) | 3/3 | 496 (374-954) |
| t3-async | vanilla | 3 | 153,920 (153,780-194,258) | 2,570 (2,496-2,881) | 0.081 (0.080-0.091) | 6 (6-6) | 29.3 (27.3-32.0) | 39,829 (39,763-40,201) | 3/3 | 986 (917-1,039) |
| t4-multi | betta | 3 | 165,834 (165,786-166,317) | 2,786 (2,767-2,806) | 0.101 (0.101-0.101) | 9 (9-9) | 53.3 (50.4-58.4) | 43,803 (43,781-43,909) | 3/3 | 1,010 (981-1,012) |
| t4-multi | betta-guide | 3 | 158,585 (158,194-158,758) | 2,832 (2,612-3,050) | 0.088 (0.087-0.089) | 6 (6-6) | 49.8 (38.0-79.3) | 40,900 (40,786-40,938) | 3/3 | 1,079 (1,035-1,083) |
| t4-multi | betta-guide-skill | 3 | 168,140 (167,414-209,801) | 2,902 (2,779-2,943) | 0.103 (0.102-0.106) | 8 (8-8) | 63.2 (38.3-120.9) | 43,779 (43,503-44,215) | 3/3 | 1,079 (1,047-1,084) |
| t4-multi | betta-noskill | 3 | 367,423 (324,273-402,064) | 4,920 (3,745-5,533) | 0.144 (0.137-0.158) | 11 (10-12) | 56.5 (46.0-62.0) | 45,446 (44,609-46,186) | 3/3 | 1,104 (1,052-1,242) |
| t4-multi | vanilla | 3 | 253,249 (213,152-256,320) | 8,670 (8,118-9,643) | 0.181 (0.171-0.191) | 11 (11-12) | 67.3 (64.6-68.9) | 47,137 (46,105-48,181) | 3/3 | 2,407 (2,350-2,483) |
| t5-refactor | betta | 3 | 247,318 (246,562-529,965) | 2,698 (2,248-4,605) | 0.108 (0.101-0.182) | 11 (10-18) | 62.0 (62.0-104.3) | 43,846 (43,012-51,014) | 3/3 | 166 (166-252) |
| t5-refactor | betta-guide | 3 | 273,196 (194,661-356,143) | 2,281 (2,237-2,626) | 0.096 (0.082-0.110) | 9 (9-14) | 74.2 (43.2-107.6) | 41,055 (40,158-41,591) | 3/3 | 183 (162-190) |
| t5-refactor | betta-guide-skill | 3 | 328,736 (245,269-415,655) | 2,849 (2,424-3,162) | 0.120 (0.103-0.133) | 10 (9-12) | 111.5 (84.6-115.5) | 44,298 (43,169-44,762) | 3/3 | 183 (144-204) |
| t5-refactor | betta-noskill | 3 | 153,018 (152,061-314,538) | 2,143 (1,768-2,510) | 0.074 (0.070-0.104) | 7 (6-11) | 58.6 (30.2-198.6) | 39,241 (38,827-41,359) | 3/3 | 190 (190-260) |
| t5-refactor | vanilla | 3 | 196,467 (194,773-196,972) | 2,851 (2,079-2,864) | 0.093 (0.083-0.097) | 11 (8-13) | 64.8 (48.2-66.1) | 41,298 (40,330-41,869) | 3/3 | 194 (190-197) |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta | 932,719 | 9,885 | 0.446 | 44 | 235.1 | 2,075 |
| betta-guide | 891,716 | 9,151 | 0.382 | 31 | 243.0 | 2,127 |
| betta-guide-skill | 959,191 | 9,889 | 0.423 | 34 | 268.3 | 2,120 |
| betta-noskill | 1,404,438 | 13,778 | 0.526 | 46 | 243.3 | 2,289 |
| vanilla | 907,647 | 17,306 | 0.493 | 41 | 219.9 | 4,556 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t1-remap | betta | 1.04x | 0.87x | 1.09x | 1.14x | 0.46x |
| t1-remap | betta-guide | 1.01x | 0.85x | 0.96x | 0.86x | 0.44x |
| t1-remap | betta-guide-skill | 1.01x | 0.82x | 0.97x | 0.86x | 0.44x |
| t1-remap | betta-noskill | 0.99x | 0.82x | 0.90x | 0.86x | 0.43x |
| t2-divide | betta | 1.31x | 0.93x | 1.17x | 1.33x | 0.61x |
| t2-divide | betta-guide | 1.01x | 0.81x | 0.95x | 0.83x | 0.56x |
| t2-divide | betta-guide-skill | 1.01x | 0.81x | 0.97x | 0.83x | 0.56x |
| t2-divide | betta-noskill | 1.00x | 0.89x | 0.95x | 1.00x | 0.60x |
| t3-async | betta | 1.05x | 0.59x | 0.99x | 1.33x | 0.39x |
| t3-async | betta-guide | 1.00x | 0.53x | 0.82x | 0.83x | 0.38x |
| t3-async | betta-guide-skill | 1.00x | 0.59x | 0.83x | 0.83x | 0.38x |
| t3-async | betta-noskill | 3.78x | 1.55x | 2.23x | 2.67x | 0.50x |
| t4-multi | betta | 0.65x | 0.32x | 0.56x | 0.82x | 0.42x |
| t4-multi | betta-guide | 0.63x | 0.33x | 0.48x | 0.55x | 0.45x |
| t4-multi | betta-guide-skill | 0.66x | 0.33x | 0.57x | 0.73x | 0.45x |
| t4-multi | betta-noskill | 1.45x | 0.57x | 0.80x | 1.00x | 0.46x |
| t5-refactor | betta | 1.26x | 0.95x | 1.16x | 1.00x | 0.86x |
| t5-refactor | betta-guide | 1.39x | 0.80x | 1.03x | 0.82x | 0.94x |
| t5-refactor | betta-guide-skill | 1.67x | 1.00x | 1.28x | 0.91x | 0.94x |
| t5-refactor | betta-noskill | 0.78x | 0.75x | 0.79x | 0.64x | 0.98x |
| ALL | betta | 1.03x | 0.57x | 0.90x | 1.07x | 0.46x |
| ALL | betta-guide | 0.98x | 0.53x | 0.78x | 0.76x | 0.47x |
| ALL | betta-guide-skill | 1.06x | 0.57x | 0.86x | 0.83x | 0.47x |
| ALL | betta-noskill | 1.55x | 0.80x | 1.07x | 1.12x | 0.50x |

## Skill effect: betta vs betta-noskill (median ratio; below 1.00x means the skill helps)

| Task | Comparison | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t1-remap | betta vs betta-noskill | 1.05x | 1.07x | 1.21x | 1.33x | 1.06x |
| t2-divide | betta vs betta-noskill | 1.32x | 1.04x | 1.23x | 1.33x | 1.01x |
| t3-async | betta vs betta-noskill | 0.28x | 0.38x | 0.45x | 0.50x | 0.77x |
| t4-multi | betta vs betta-noskill | 0.45x | 0.57x | 0.70x | 0.82x | 0.91x |
| t5-refactor | betta vs betta-noskill | 1.62x | 1.26x | 1.46x | 1.57x | 0.87x |
| ALL | betta vs betta-noskill | 0.66x | 0.72x | 0.85x | 0.96x | 0.91x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

