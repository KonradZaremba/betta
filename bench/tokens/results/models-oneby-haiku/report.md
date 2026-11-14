# Token benchmark report

- Date: 2026-11-14 11:03
- Model requested: haiku (models seen in results: claude-haiku-5-5)
- Runs per task x arm: 3
- Max turns: 40; max budget per run: $3
- Arms: betta-guide, vanilla
- Repo commit (skill version): 89859bc
- Isolation flags: `--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools Read Edit Write Glob Grep Bash(dotnet build:*) Bash(dotnet restore:*) PowerShell(dotnet build:*) PowerShell(dotnet restore:*) Bash(ls:*) Bash(cat:*) Bash(head:*) Bash(tail:*) Bash(grep:*) Bash(find:*)`
- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).

## Per task and arm (median (min-max))

| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens | GUID kept |
|---|---|---|---|---|---|---|---|---|---|---|---|
| t1-remap | betta-guide | 3 | 206,073 (205,458-206,161) | 2,369 (2,120-2,450) | 0.006 (0.006-0.006) | 7 (7-7) | 18.2 (16.7-19.1) | 42,309 (42,170-42,331) | 3/3 | 235 (214-245) | n/a |
| t1-remap | vanilla | 3 | 366,577 (284,806-380,428) | 3,417 (3,170-4,601) | 0.008 (0.007-0.009) | 10 (7-11) | 33.1 (31.1-35.5) | 43,000 (42,692-44,969) | 3/3 | 548 (489-551) | n/a |
| t2-divide | betta-guide | 3 | 208,739 (207,540-422,733) | 3,359 (3,291-4,217) | 0.006 (0.006-0.009) | 9 (7-10) | 30.9 (25.9-41.1) | 43,576 (43,265-44,558) | 3/3 | 368 (364-371) | n/a |
| t2-divide | vanilla | 3 | 333,770 (205,335-371,940) | 3,491 (3,012-3,618) | 0.008 (0.006-0.008) | 8 (7-9) | 29.4 (23.0-31.3) | 43,719 (43,642-44,113) | 3/3 | 524 (522-531) | n/a |
| t3-async | betta-guide | 3 | 164,593 (163,636-164,834) | 3,220 (2,879-3,279) | 0.006 (0.006-0.006) | 5 (5-6) | 20.8 (18.9-21.7) | 42,556 (41,961-42,566) | 3/3 | 445 (427-505) | n/a |
| t3-async | vanilla | 3 | 464,613 (444,469-624,364) | 10,734 (8,898-16,207) | 0.014 (0.012-0.020) | 10 (10-16) | 63.6 (55.6-97.2) | 50,649 (48,212-58,168) | 3/3 | 1,395 (1,178-1,442) | n/a |
| t6-shelfpack | betta-guide | 3 | 551,845 (519,676-587,532) | 15,106 (14,282-16,155) | 0.020 (0.017-0.020) | 14 (12-14) | 79.0 (78.2-84.2) | 62,438 (54,717-63,966) | 3/3 | 1,847 (1,774-1,997) | n/a |
| t6-shelfpack | vanilla | 3 | 458,374 (428,207-530,012) | 17,451 (14,560-19,566) | 0.019 (0.017-0.020) | 12 (8-16) | 85.1 (74.2-93.2) | 57,438 (55,249-59,464) | 3/3 | 2,312 (2,248-2,330) | n/a |
| t7-panels | betta-guide | 3 | 495,011 (441,250-674,315) | 11,403 (9,333-14,336) | 0.017 (0.015-0.021) | 12 (10-19) | 60.1 (52.9-78.8) | 61,272 (60,468-64,548) | 3/3 | 1,394 (1,348-1,472) | n/a |
| t7-panels | vanilla | 3 | 386,729 (351,725-416,422) | 10,804 (7,247-13,384) | 0.014 (0.011-0.015) | 8 (8-11) | 57.5 (40.8-64.4) | 50,907 (47,539-53,752) | 3/3 | 1,776 (1,720-1,782) | n/a |

## Per-arm total (sum of per-task medians)

| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |
|---|---|---|---|---|---|---|
| betta-guide | 1,626,261 | 35,457 | 0.054 | 47 | 209.0 | 4,289 |
| vanilla | 2,010,063 | 45,897 | 0.062 | 48 | 268.7 | 6,555 |

## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)

| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |
|---|---|---|---|---|---|---|
| t1-remap | betta-guide | 0.56x | 0.69x | 0.73x | 0.70x | 0.43x |
| t2-divide | betta-guide | 0.63x | 0.96x | 0.82x | 1.13x | 0.70x |
| t3-async | betta-guide | 0.35x | 0.30x | 0.41x | 0.50x | 0.32x |
| t6-shelfpack | betta-guide | 1.20x | 0.87x | 1.07x | 1.17x | 0.80x |
| t7-panels | betta-guide | 1.28x | 1.06x | 1.21x | 1.50x | 0.78x |
| ALL | betta-guide | 0.81x | 0.77x | 0.88x | 0.98x | 0.65x |

## Reference implementation size (hand-written, minimal, idiomatic)

chars include one newline per line; tokens are est. = ceil(chars / 4).

| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |
|---|---|---|---|---|---|---|---|
| Remap | 22 | 870 | 218 | 42 | 1778 | 445 | 0.49x |
| DivideCurveStats | 24 | 925 | 232 | 50 | 1875 | 469 | 0.49x |
| AsyncStats | 34 | 1183 | 296 | 73 | 2657 | 665 | 0.45x |
| **Total** | 80 | 2978 | 746 | 165 | 6310 | 1579 | 0.47x |

