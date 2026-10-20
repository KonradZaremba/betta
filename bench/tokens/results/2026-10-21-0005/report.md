# Token benchmark report

- Date: 2026-10-21 00:05
- Model requested: sonnet (models seen in results: claude-haiku-5-5;claude-sonnet-5-5)
- Runs per task x arm: 3
- Max turns: 40; max budget per run: $3; skill mode (betta arm): skill
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens |
|---|---|---|---|---|---|---|---|---|---|---|
| t1-remap | betta | 3 | 177,678 (176,746-223,401) | 1,416 (1,413-1,474) | 0.103 (0.101-0.105) | 9 (9-9) | 18.9 (17.5-19.5) | 46,932 (46,928-47,346) | 3/3 | 217 (217-221) |
| t1-remap | vanilla | 3 | 150,957 (150,922-151,275) | 1,537 (1,493-1,566) | 0.066 (0.066-0.067) | 5 (5-6) | 22.1 (20.8-28.0) | 38,438 (38,435-38,530) | 3/3 | 481 (475-481) |
| t2-divide | betta | 3 | 223,481 (177,485-223,591) | 1,451 (1,421-1,546) | 0.107 (0.102-0.108) | 9 (8-9) | 23.9 (23.7-24.9) | 47,266 (47,148-47,290) | 3/3 | 287 (287-302) |
| t2-divide | vanilla | 3 | 151,777 (151,737-151,785) | 1,503 (1,502-1,513) | 0.068 (0.067-0.068) | 6 (6-6) | 24.2 (18.0-26.1) | 38,881 (38,791-38,886) | 3/3 | 456 (455-456) |
| t3-async | betta | 3 | 178,378 (177,911-224,704) | 1,722 (1,591-1,857) | 0.106 (0.104-0.107) | 8 (8-9) | 21.2 (19.3-22.4) | 47,239 (47,208-47,534) | 3/3 | 299 (289-367) |
| t3-async | vanilla | 3 | 153,837 (153,751-154,167) | 2,530 (2,506-2,644) | 0.082 (0.080-0.084) | 6 (6-6) | 31.4 (28.8-38.3) | 39,792 (39,721-40,017) | 3/3 | 898 (839-1,002) |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta | 579,537 | 4,589 | 0.316 | 26 | 64.0 | 803 |
| vanilla | 456,571 | 5,570 | 0.215 | 17 | 77.7 | 1,835 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t1-remap | betta | 1.18x | 0.92x | 1.56x | 1.80x | 0.45x |
| t2-divide | betta | 1.47x | 0.97x | 1.58x | 1.50x | 0.63x |
| t3-async | betta | 1.16x | 0.68x | 1.30x | 1.33x | 0.33x |
| ALL | betta | 1.27x | 0.82x | 1.47x | 1.53x | 0.44x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

