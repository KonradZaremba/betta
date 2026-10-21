# Token benchmark: Betta vs plain GH_Component

Measures what a headless Claude Code agent spends (tokens, dollars, turns, wall time) to add the same
Grasshopper component(s) to (a) a Betta plugin project, with and without the betta-plugin skill, and (b) a hand-written `GH_Component` plugin project,
plus how much code the result contains.

Latest results and what they mean: [RESULTS.md](RESULTS.md).

**Real runs bill your Claude usage.** Each run is capped by `-MaxBudgetUsd` (default 3) and `-MaxTurns`
(default 40), but a full run is 5 tasks x 3 arms x 3 runs = 45 sessions, all billed to your Claude usage. Dry-run first.

## What is measured

- Per run (`runs.csv`): exit code, result subtype, turns, duration, cost, tokens (summed from `modelUsage`:
  input, cache creation, cache read, output; top-level `usage` if `modelUsage` is absent), and
  `final_context_tokens` (context size of the last API call, from the last `usage.iterations` element).
- Independently of the agent: `dotnet build -c Release` in the workspace (pass/fail, error count).
- Code the agent added: `git diff --no-index -U0` of scaffold+seed vs workspace (ignoring `bin/`, `obj/`, `.claude/`):
  files changed, added lines, added chars, `added_est_tokens` = ceil(chars / 4). **est.** only; not a tokenizer.
- Reference implementations (`reference/`): the minimal idiomatic code for each task in each style, measured
  the same way. This is the model-free code-size comparison. Betta references use the terse class-direct style;
  the scaffold itself uses the interface style, so agents may legitimately write more than the reference.

Tasks (`tasks/`; prompt text is identical for every arm):

| Task | What |
|---|---|
| `t1-remap` | add "Remap" (Math) |
| `t2-divide` | add "Divide Curve Stats" (Geometry) |
| `t3-async` | add "Async Stats" (Data, async) |
| `t4-multi` | add five components in one go: Remap, Clamp, Lerp, Divide Curve Stats, Async Stats |
| `t5-refactor` | change the existing "Remap": rename input, add a boolean input and a second output, move subcategory |

Arms (`-Arms`, default `betta betta-noskill vanilla`; an unknown arm throws):

- `betta`: `scaffolds/betta` plus the whole `.claude/skills/betta-plugin/` folder (SKILL.md and `references/`).
- `betta-noskill`: `scaffolds/betta` with no `.claude` folder.
- `vanilla`: `scaffolds/vanilla` (plain `GH_Component`).

Seeds: a task may have `tasks/<task>.seed/<scaffold>/` (scaffold is `betta` or `vanilla`; `betta-noskill` uses
`betta`). Its files are copied over the scaffold before the agent runs. `t5-refactor` seeds the existing `Remap.cs`
(a copy of `reference/<arm>/Remap.cs`). A base dir (scaffold + seed) is built once per task and scaffold under
`C:\tmp\betta-bench\<runId>\_base\`; measurement diffs against it, so an untouched seeded workspace shows 0 added
lines. For `t5` the added-code numbers are therefore the size of the change, not of a whole component.

## Layout

- `scaffolds/betta` (BenchPlugin from the `bettaplugin-basic` template, Betta.Abstractions 0.7.2.1, RhinoCommon added)
  and `scaffolds/vanilla` (plain `GH_Component`, Grasshopper `[8.0,8.27)`). Both build untouched.
- `run.ps1` runs, `measure.ps1` measures, `report.ps1` writes `report.md`, `common.ps1` has shared helpers.

## How to run

```powershell
# 1. dry run: prepares workspaces, prints the claude argument lists, builds and measures the untouched scaffolds
powershell -NoProfile -File bench/tokens/run.ps1 -DryRun -Runs 1

# 2. real run (bills your usage)
powershell -NoProfile -File bench/tokens/run.ps1 -Runs 3
powershell -NoProfile -File bench/tokens/run.ps1 -Tasks t1 -Arms betta,betta-noskill -Runs 1   # subset of tasks and arms
```

Output goes to `bench/tokens/results/<yyyy-MM-dd-HHmm>/` (`runs.csv`, `report.md`, `meta.json`, `raw/*.json`,
`raw/*.err.txt`). Regenerate the report with `report.ps1 -OutDir <folder>`. `meta.json` records the arms and the repo
commit hash (the skill version used). Results folders are committed later; workspaces never are.

## Isolation and its limits

- Workspaces are fresh copies under `C:\tmp\betta-bench\<runId>\`, outside `C:\Users\konra`, so no ancestor
  `CLAUDE.md` loads.
- `--setting-sources project,local` (no user settings, skills or hooks), `--strict-mcp-config` with an empty
  config (no MCP servers), `--no-session-persistence`, and a tool allow-list (`Read Edit Write Glob Grep` and
  `Bash(dotnet build:*)`). stdin is closed on each call because `claude -p` otherwise waits 3 s for it.
- The `betta` arm gets a recursive copy of `.claude/skills/betta-plugin/` with, in every `.md`, `Version="0.8.0"` rewritten to
  `0.7.2.1` (0.8.0 is not on nuget.org yet). The other arms get no skill.
- Known limits: model nondeterminism (hence several runs and medians); prompt caching makes `cache_read` volume
  and cost depend on timing; both arms share the model's prior knowledge of the Grasshopper SDK, but only Betta
  is a niche library, so the skill matters (compare `betta` with `betta-noskill`); NuGet restore of the first run may be
  slower; the allow-list stops the agent from running anything but `dotnet build`, so it cannot test in Rhino.
- `Process`-based launching builds the argument string with standard Windows quoting, so the prompt reaches
  `claude` as a single argument.
