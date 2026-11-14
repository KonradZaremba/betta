# Token benchmark report

- Date: 2026-11-14 10:42
- Model requested: opus (models seen in results: claude-haiku-5-5;claude-opus-5-5)
- Runs per task x arm: 3
- Max turns: 40; max budget per run: $3
- Arms: betta-guide, vanilla
- Repo commit (skill version): 99ef6b5
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens | GUID kept |
|---|---|---|---|---|---|---|---|---|---|---|---|
| t1-remap | betta-guide | 3 | 359,022 (237,342-490,819) | 2,401 (1,937-2,878) | 0.217 (0.171-0.273) | 11 (8-14) | 42.7 (29.4-49.6) | 42,204 (40,591-44,781) | 3/3 | 247 (240-260) | n/a |
| t1-remap | vanilla | 3 | 154,881 (154,603-232,049) | 1,847 (1,736-1,852) | 0.145 (0.142-0.162) | 6 (5-7) | 23.4 (23.3-29.5) | 39,625 (39,509-39,838) | 3/3 | 531 (531-531) | n/a |
| t2-divide | betta-guide | 3 | 275,997 (196,783-321,245) | 1,877 (1,777-2,524) | 0.179 (0.158-0.210) | 9 (7-12) | 27.9 (24.5-36.6) | 40,884 (40,419-42,003) | 3/3 | 321 (305-334) | n/a |
| t2-divide | vanilla | 3 | 154,897 (154,416-192,506) | 1,660 (1,597-1,859) | 0.140 (0.138-0.154) | 5 (5-7) | 23.0 (22.0-24.9) | 39,512 (39,451-39,755) | 3/3 | 538 (517-565) | n/a |
| t3-async | betta-guide | 3 | 196,871 (196,859-201,670) | 2,011 (1,875-2,985) | 0.160 (0.158-0.193) | 7 (7-8) | 27.1 (26.1-32.4) | 40,410 (40,323-42,047) | 3/3 | 371 (335-389) | n/a |
| t3-async | vanilla | 3 | 200,655 (198,846-241,632) | 4,768 (3,935-5,191) | 0.238 (0.209-0.256) | 6 (5-8) | 52.6 (41.1-55.0) | 42,984 (41,717-43,484) | 3/3 | 1,582 (1,531-1,735) | n/a |
| t6-shelfpack | betta-guide | 3 | 293,393 (250,816-327,772) | 3,958 (3,904-4,557) | 0.250 (0.238-0.264) | 8 (7-10) | 48.2 (46.9-51.0) | 43,779 (43,518-44,446) | 3/3 | 1,396 (1,329-1,480) | n/a |
| t6-shelfpack | vanilla | 3 | 243,981 (200,624-245,182) | 4,875 (4,678-5,375) | 0.252 (0.235-0.265) | 8 (7-8) | 50.0 (48.7-53.6) | 43,461 (42,858-43,747) | 3/3 | 2,022 (1,889-2,132) | n/a |
| t7-panels | betta-guide | 3 | 367,190 (283,907-369,176) | 3,743 (3,291-4,583) | 0.253 (0.227-0.277) | 10 (9-11) | 47.0 (42.5-51.8) | 43,264 (43,180-44,098) | 3/3 | 1,198 (1,023-1,246) | n/a |
| t7-panels | vanilla | 3 | 160,793 (160,454-239,655) | 3,770 (3,752-4,122) | 0.209 (0.201-0.217) | 5 (5-7) | 41.1 (38.8-42.9) | 41,922 (41,789-41,972) | 3/3 | 1,581 (1,516-1,687) | n/a |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide | 1,492,473 | 13,990 | 1.059 | 45 | 192.8 | 3,533 |
| vanilla | 915,207 | 16,920 | 0.984 | 30 | 190.1 | 6,254 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t1-remap | betta-guide | 2.32x | 1.30x | 1.50x | 1.83x | 0.47x |
| t2-divide | betta-guide | 1.78x | 1.13x | 1.28x | 1.80x | 0.60x |
| t3-async | betta-guide | 0.98x | 0.42x | 0.67x | 1.17x | 0.23x |
| t6-shelfpack | betta-guide | 1.20x | 0.81x | 0.99x | 1.00x | 0.69x |
| t7-panels | betta-guide | 2.28x | 0.99x | 1.21x | 2.00x | 0.76x |
| ALL | betta-guide | 1.63x | 0.83x | 1.08x | 1.50x | 0.56x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

