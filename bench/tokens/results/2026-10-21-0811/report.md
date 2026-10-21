# Token benchmark report

- Date: 2026-10-21 08:11
- Model requested: sonnet (models seen in results: claude-haiku-5-5;claude-sonnet-5-5)
- Runs per task x arm: 3
- Max turns: 40; max budget per run: $3
- Arms: betta, betta-noskill, vanilla
- Repo commit (skill version): ad3a908
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens |
|---|---|---|---|---|---|---|---|---|---|---|
| t1-remap | betta | 3 | 158,562 (158,091-158,591) | 1,367 (1,160-1,475) | 0.075 (0.071-0.075) | 8 (7-8) | 34.8 (29.8-40.5) | 40,799 (40,561-40,851) | 3/3 | 221 (221-229) |
| t1-remap | betta-noskill | 3 | 150,958 (150,758-151,352) | 1,360 (1,191-1,410) | 0.063 (0.062-0.066) | 6 (6-7) | 32.6 (28.8-35.4) | 38,304 (38,301-38,663) | 3/3 | 210 (210-215) |
| t1-remap | vanilla | 3 | 152,151 (151,895-152,210) | 1,667 (1,640-1,679) | 0.069 (0.069-0.070) | 7 (6-7) | 32.2 (31.1-32.7) | 38,924 (38,853-38,971) | 3/3 | 487 (475-488) |
| t2-divide | betta | 3 | 159,468 (158,448-199,204) | 1,340 (1,339-1,369) | 0.076 (0.074-0.080) | 8 (7-8) | 29.7 (28.5-37.6) | 41,204 (40,713-41,205) | 3/3 | 284 (267-287) |
| t2-divide | betta-noskill | 3 | 151,210 (151,121-151,222) | 1,375 (1,330-1,382) | 0.065 (0.064-0.065) | 6 (6-6) | 23.6 (23.3-23.7) | 38,501 (38,476-38,520) | 3/3 | 289 (251-289) |
| t2-divide | vanilla | 3 | 151,860 (151,839-151,880) | 1,548 (1,546-1,558) | 0.068 (0.068-0.068) | 6 (6-6) | 26.3 (26.1-26.5) | 38,918 (38,894-38,927) | 3/3 | 482 (456-482) |
| t3-async | betta | 3 | 160,714 (160,590-202,107) | 1,480 (1,456-1,523) | 0.080 (0.079-0.084) | 8 (8-8) | 29.3 (23.6-30.3) | 41,741 (41,699-41,770) | 3/3 | 323 (323-383) |
| t3-async | betta-noskill | 3 | 581,829 (543,114-630,517) | 3,980 (3,493-5,530) | 0.180 (0.180-0.189) | 16 (16-17) | 71.9 (67.2-89.2) | 50,339 (49,819-50,675) | 3/3 | 496 (374-954) |
| t3-async | vanilla | 3 | 153,920 (153,780-194,258) | 2,570 (2,496-2,881) | 0.081 (0.080-0.091) | 6 (6-6) | 29.3 (27.3-32.0) | 39,829 (39,763-40,201) | 3/3 | 986 (917-1,039) |
| t4-multi | betta | 3 | 166,174 (165,851-166,549) | 2,995 (2,866-3,176) | 0.103 (0.102-0.103) | 9 (9-9) | 31.3 (28.1-53.9) | 43,880 (43,860-43,960) | 3/3 | 1,016 (954-1,101) |
| t4-multi | betta-noskill | 3 | 367,423 (324,273-402,064) | 4,920 (3,745-5,533) | 0.144 (0.137-0.158) | 11 (10-12) | 56.5 (46.0-62.0) | 45,446 (44,609-46,186) | 3/3 | 1,104 (1,052-1,242) |
| t4-multi | vanilla | 3 | 253,249 (213,152-256,320) | 8,670 (8,118-9,643) | 0.181 (0.171-0.191) | 11 (11-12) | 67.3 (64.6-68.9) | 47,137 (46,105-48,181) | 3/3 | 2,407 (2,350-2,483) |
| t5-refactor | betta | 3 | 285,117 (201,407-285,596) | 2,868 (2,122-3,052) | 0.110 (0.091-0.112) | 10 (8-11) | 47.3 (36.4-48.0) | 42,750 (41,924-42,998) | 3/3 | 201 (190-256) |
| t5-refactor | betta-noskill | 3 | 153,018 (152,061-314,538) | 2,143 (1,768-2,510) | 0.074 (0.070-0.104) | 7 (6-11) | 58.6 (30.2-198.6) | 39,241 (38,827-41,359) | 3/3 | 190 (190-260) |
| t5-refactor | vanilla | 3 | 196,467 (194,773-196,972) | 2,851 (2,079-2,864) | 0.093 (0.083-0.097) | 11 (8-13) | 64.8 (48.2-66.1) | 41,298 (40,330-41,869) | 3/3 | 194 (190-197) |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta | 930,035 | 10,050 | 0.443 | 43 | 172.5 | 2,045 |
| betta-noskill | 1,404,438 | 13,778 | 0.526 | 46 | 243.3 | 2,289 |
| vanilla | 907,647 | 17,306 | 0.493 | 41 | 219.9 | 4,556 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t1-remap | betta | 1.04x | 0.82x | 1.08x | 1.14x | 0.45x |
| t1-remap | betta-noskill | 0.99x | 0.82x | 0.90x | 0.86x | 0.43x |
| t2-divide | betta | 1.05x | 0.87x | 1.11x | 1.33x | 0.59x |
| t2-divide | betta-noskill | 1.00x | 0.89x | 0.95x | 1.00x | 0.60x |
| t3-async | betta | 1.04x | 0.58x | 0.99x | 1.33x | 0.33x |
| t3-async | betta-noskill | 3.78x | 1.55x | 2.23x | 2.67x | 0.50x |
| t4-multi | betta | 0.66x | 0.35x | 0.57x | 0.82x | 0.42x |
| t4-multi | betta-noskill | 1.45x | 0.57x | 0.80x | 1.00x | 0.46x |
| t5-refactor | betta | 1.45x | 1.01x | 1.17x | 0.91x | 1.04x |
| t5-refactor | betta-noskill | 0.78x | 0.75x | 0.79x | 0.64x | 0.98x |
| ALL | betta | 1.02x | 0.58x | 0.90x | 1.05x | 0.45x |
| ALL | betta-noskill | 1.55x | 0.80x | 1.07x | 1.12x | 0.50x |

## Skill effect: betta vs betta-noskill (median ratio; below 1.00x means the skill helps)

| Task | Comparison | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t1-remap | betta vs betta-noskill | 1.05x | 1.01x | 1.19x | 1.33x | 1.05x |
| t2-divide | betta vs betta-noskill | 1.05x | 0.97x | 1.17x | 1.33x | 0.98x |
| t3-async | betta vs betta-noskill | 0.28x | 0.37x | 0.44x | 0.50x | 0.65x |
| t4-multi | betta vs betta-noskill | 0.45x | 0.61x | 0.72x | 0.82x | 0.92x |
| t5-refactor | betta vs betta-noskill | 1.86x | 1.34x | 1.48x | 1.43x | 1.06x |
| ALL | betta vs betta-noskill | 0.66x | 0.73x | 0.84x | 0.93x | 0.89x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

