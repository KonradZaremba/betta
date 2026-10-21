# Token benchmark results

A headless Claude Code agent (Claude Sonnet 5.5) makes the same change in fresh projects, isolated from user settings. Every session's result is then built independently.

| Run | What changed | Sessions | Report |
|---|---|---|---|
| **7 (latest)** — 2026-10-21 14:10 | Everything re-run fresh with the current guide, which adds "throw for errors, `IBettaReport` for warnings" and "pin a GUID on components with generic signatures". All 7 tasks, guide vs plain, plus t5 with Betta.Mcp. Betta.Abstractions `0.8.0-bench.1` | 7 tasks × 2 arms × 3 runs + 3 = 45, all build | [report](results/2026-10-21-1410/report.md) |
| 6 — 2026-10-21 13:14 | Run 5's two tasks again, after adding `IBettaReport` (warn and still output) to Betta and two lines on errors and warnings to the guide. Betta.Abstractions is a local `0.8.0-bench.1` pack, since the API isn't published yet. `vanilla` rows are copied from run 5 | 6 new + 6 reused, all build | [report](results/2026-10-21-1314/report.md) |
| 5 — 2026-10-21 12:24 | Two bigger components with real logic: `t6-shelfpack` (packing algorithm) and `t7-panels` (surface panelling). Each must show an error for bad input and a warning that still outputs results. Guide only vs plain `GH_Component` | 2 tasks × 2 arms × 3 runs = 12, all build | [report](results/2026-10-21-1224/report.md) |
| 4 — 2026-10-21 12:01 | The refactor task with the guide **plus Betta.Mcp**, pointed at a Libraries folder holding the published build. The guide now names `betta_describe`. New automated check: does the component still publish its original GUID? `betta-guide` (run 3) and `vanilla` (run 2) t5 rows are copied in and checked the same way | 6 new + 6 reused, all build | [report](results/2026-10-21-1201/report.md) |
| 3 — 2026-10-21 09:26 | The `dotnet new` templates' agent guide (`CLAUDE.md`, ~450 tokens), alone and with the skill. Skill GUID wording changed, so `betta` is re-run. `betta-noskill` and `vanilla` rows are copied from run 2 (same model, day, scaffolds and tasks) | 45 new + 30 reused, all build | [report](results/2026-10-21-0926/report.md) |
| 2 — 2026-10-21 08:11, commit `ad3a908` | Skill split into a 6k-char core + references. New arms and tasks: Betta **without** the skill, five components in one prompt, and a refactor | 5 tasks × 3 arms × 3 runs = 45, all build | [report](results/2026-10-21-0811/report.md) |
| 1 — 2026-10-21 00:05 | Original 21.5k-char skill | 3 tasks × 2 arms × 3 runs = 18, all build | [report](results/2026-10-21-0005/report.md) |

Context-cost calibration for the skill and Betta.Mcp: [results/calibration/calibration.md](results/calibration/calibration.md).

## Run 7: the GUID-pinning rule costs turns

Every arm re-run in one go with the guide as it now ships. The plain `GH_Component` costs reproduced this morning's within 3% (t4: $0.179 vs $0.181), so the differences below come from the guide.

| Task (median) | Betta cost | Plain cost | Ratio | Run 3/6 ratio (older guide) | Betta GUID pins |
|---|---|---|---|---|---|
| t1 Remap | 0.070 | 0.069 | 1.01x | 0.96x | 0 (no generics) |
| t2 Divide curve | 0.100 | 0.067 | **1.48x** | 0.95x | 1 per run |
| t3 Async stats | 0.098 | 0.079 | **1.24x** | 0.82x | 1 per run |
| t4 Five components | 0.114 | 0.179 | **0.64x** | 0.48x | 2 per run |
| t5 Refactor | 0.111 | 0.096 | 1.15x | 1.03x | GUID kept 0/3 |
| t5 Refactor + Betta.Mcp | 0.093 | 0.096 | **0.96x** | 0.93x | GUID kept 3/3 |
| t6 Shelf Pack | 0.110 | 0.106 | 1.03x | 1.10x | 1 per run |
| t7 Panel Grid | 0.112 | 0.094 | 1.19x | 1.02x | 1 per run |
| **All 7** (guide; t5 without MCP) | | | **1.03x** | | |

Code written: 0.64x of the plain route overall.

- **Agents follow the new rule:** every new component with a generic signature got a pinned GUID, and none without one.
- **That's where the extra cost comes from.** To make a "proper" random GUID, most agents tried `[guid]::NewGuid()`, Python's `uuid` or `/proc/sys/kernel/random/uuid`. The benchmark doesn't allow those tools, so each attempt was denied and cost turns: 1–4 denials per run in t2–t4. Plain-GH agents, and the t6 Betta agents, just typed a GUID inline at no extra cost. In a normal session the tool would usually be allowed, but it's still an extra turn.
- **The trade:** the older guide was cheaper (about 0.85x over the same tasks), but it left GUIDs that change with every plugin, Rhino or .NET update. The current guide costs about the same as hand-written code and keeps saved files working.
- **Ways to get the saving back:** tell agents to type the new GUID inline, or, better, make derived GUIDs version-independent (ROADMAP) so new components need no pin at all.

## Run 6: with a way to warn, bigger components break even

Run 5 showed the gap; run 6 measures the fix. Betta gained `IBettaReport`, a parameter Betta fills in like `CancellationToken`, and `report.Warning("…")` shows on the component while the outputs are still set. The template guide gained one bullet: throw for an error with no outputs, take an `IBettaReport` to warn and still output.

| Guide vs plain `GH_Component` (median) | Cost | Turns | Code written | Warning shown |
|---|---|---|---|---|
| t6 Shelf Pack | **1.10x** (run 5: 2.51x) | 8 vs 6 (22) | 0.78x | **3/3** (0/3) |
| t7 Panel Grid | **1.02x** (run 5: 2.46x) | 7 vs 6 (21) | 0.77x | **3/3** (0/3) |
| Both | **1.06x** (run 5: 2.49x) | 1.25x (3.58x) | 0.77x | 6/6 (0/6) |

- **All six agents used it as intended:** an `IBettaReport report` parameter, `throw` for bad input, and `report.Warning(…)` when parts are left out or panels aren't flat, with the outputs still returned.
- **Bigger components are now about even on cost and a quarter smaller in code.** With most of the code being the algorithm itself, Betta's saving is the wiring, which is a smaller share than in the small tasks.
- **Not re-measured:** t1–t5 with the slightly longer guide (about 60 more tokens of context). Those tasks don't need warnings, so the effect should be small.
- **Not tested inside Grasshopper:** the benchmark only builds. The message collector has headless tests; posting the messages on the canvas still needs a check in Rhino.

## Run 5: bigger components hit a missing API

Runs 1–4 used small components, where boilerplate is most of the code. Run 5 asks for two components of about 100 lines of logic each. Both must show an error and output nothing on bad input, and show a warning but still output everything when some parts don't fit (t6) or some panels aren't flat (t7). Every session built.

| Guide vs plain `GH_Component` (median) | Cost | Turns | Tokens | Code written | Warning shown |
|---|---|---|---|---|---|
| t6 Shelf Pack | **2.51x** | 22 vs 6 | 4.42x | 0.75x | Betta 0/3, plain 3/3 |
| t7 Panel Grid | **2.46x** | 21 vs 6 | 5.43x | 0.67x | Betta 0/3, plain 3/3 |

- **Betta has no way for a method to raise a warning and still return results.** A thrown exception becomes an error with no outputs, which fits the error case. `[GrasshopperRange]` and the other validators warn but skip the call. A hand-written component calls `AddRuntimeMessage(Warning, …)` and sets its outputs anyway.
- **The extra turns went on that gap.** All six agents concluded that the SDK has no such API, said so plainly, and left the warning out. None faked it or added a Grasshopper reference.
- **They were also unsure about errors.** All six threw an `ArgumentException`, but most said they hadn't confirmed that Betta shows it as an error. The guide doesn't say.
- **Code size still favours Betta** (0.67–0.75x), less than for small components because the logic is the same on both routes.
- **Fixed and re-measured in run 6:** `IBettaReport`, plus a guide line on errors and warnings.

## Run 4: Betta.Mcp fixes refactors

Runs 2 and 3 left one problem open. A refactor changes a Betta component's signature-derived GUID, so saved `.gh` files lose it, and agents had no way to look up the current value. Run 4 attaches Betta.Mcp, the way a user who followed the README would have it. Its `betta_describe` tool reads the deployed plugin and reports the GUID Betta publishes.

The harness now checks GUIDs automatically. After each session it builds the result and asks `betta_describe` which GUID `Remap` publishes; for plain `GH_Component` it reads `Remap.cs`'s `ComponentGuid`. The original is `8b18f440-…`, which isn't in the starter code, the prompt or the guide.

| t5 refactor (median) | Cost vs plain | Turns vs plain | Duration | Saved files keep working |
|---|---|---|---|---|
| **guide + Betta.Mcp** | **0.93x** | **0.73x** | **36 s** | **6/6** |
| guide only (run 3) | 1.03x | 0.82x | 74 s | 0/3 |
| plain `GH_Component` (run 2) | 1.00x | 1.00x | 65 s | 3/3 |

- **All six agents pinned the original GUID**, `Guid = "8b18f440-…"`, a value only `betta_describe` could have given them.
- **Cheaper too:** 0.68x the plain route's output tokens (guide only: 0.80x), fewer turns, and half the time.
- **Every earlier Betta refactor broke saved files: 0 of 15** across runs 2–3 and all setups (skill, no skill, guide, guide + skill), re-checked with the same automated check.
- **A Betta.Mcp bug found on the way.** `betta_describe` ignored a pinned `Guid` and always reported the signature hash, which would have misled an agent refactoring an already-pinned component. Fixed, with a test.
- **The harness also stopped deploying.** The starter project's post-build step copied `BenchPlugin.dll` into the real `%AppData%\Grasshopper\Libraries\Betta` on every build. A `Directory.Build.targets` above the workspaces now blanks it.

## Run 3: the template's agent guide is the cheapest correct setup

Every `dotnet new bettaplugin-*` project now ships `CLAUDE.md` and an `AGENTS.md` copy. It's a ~1.7k-char guide with the facts agents got wrong in runs 1–2: async = `Task<T>` + `CancellationToken`, `IObservable<T>`, named tuples, list and geometry inputs, categories, and the GUID refactor rule. It points to the skill for anything deeper.

Median ratios vs plain `GH_Component`, all five tasks (sum of medians):

| Betta setup | Total tokens | Cost | Output tokens | Turns | Code written |
|---|---|---|---|---|---|
| **guide (what `dotnet new` gives you)** | **0.98x** | **0.78x** | **0.53x** | **0.76x** | **0.47x** |
| guide + skill | 1.06x | 0.86x | 0.57x | 0.83x | 0.47x |
| skill only | 1.03x | 0.90x | 0.57x | 1.07x | 0.46x |
| neither (run 2) | 1.55x | 1.07x | 0.80x | 1.12x | 0.50x |

Per task, guide only: cost 0.96x (t1), 0.95x (t2), 0.82x (t3 async), **0.48x (t4 five components)** and 1.03x (t5 refactor).

- **Correct as well as cheap.** All 6 async runs with the guide returned `Task<…>` with a `CancellationToken`, and none fell back to `GH_Component`. Without any guidance, run 2 got this wrong and cost 3.8x.
- **The skill adds cost here without adding correctness** on these five tasks. It is still the place for opaque types, preview, trees and validation, which these tasks don't exercise.
- **Refactors are still the open problem.** With the new "pin the current GUID, never invent one" wording, all 6 guide runs left the GUID alone. Each told the user plainly that existing `.gh` files will break. That's honest but not a fix. The skill-only arm still invented a GUID once, though it labelled it a guess. Agents cannot get the current GUID without tooling, which is what the GUID lock on the roadmap is for.

## Run 2: Betta (lean skill) vs plain `GH_Component`

Median ratios, Betta ÷ plain (below 1.00x favours Betta):

| Task | Total tokens | Cost | Output tokens | Code written |
|---|---|---|---|---|
| t1 Remap (one small component) | 1.04x | 1.08x | 0.82x | 0.45x |
| t2 Divide curve (geometry, two outputs) | 1.05x | 1.11x | 0.87x | 0.59x |
| t3 Async stats (cancellation) | 1.04x | 0.99x | 0.58x | 0.33x |
| **t4 Five components in one prompt** | **0.66x** | **0.57x** | **0.35x** | **0.42x** |
| t5 Refactor an existing component | 1.45x | 1.17x | 1.01x | 1.04x |
| All five (sum of medians) | 1.02x | 0.90x | 0.58x | 0.45x |

**The lean skill fixed the overhead.** Loading it now adds about 2.5k tokens of context (t1/t2 final context, with vs without the skill), down from about 8.5k. On t1–t3, a single component now costs about the same as plain GH: 1.05x tokens and 1.06x cost, down from 1.27x and 1.47x in run 1. The agent still writes half the code.

**Real work amortises the skill.** Asked for five components in one go, the Betta agent used 0.66x the tokens, 0.57x the cost and 0.35x the output tokens of the plain agent, and finished in half the time.

## What the skill is worth (Betta with vs without it)

| Task | Tokens | Cost |
|---|---|---|
| t1, t2 (simple) | 1.05x | 1.17–1.19x |
| t3 async | **0.28x** | **0.44x** |
| t4 five components | **0.45x** | **0.72x** |
| t5 refactor | 1.86x | 1.48x |
| All | 0.66x | 0.84x |

On simple components the project's existing pattern is enough, and the skill is a small tax. On async it is the difference between right and wrong. Without it, every run spent about 16 turns. One agent concluded that *"Betta (0.7.2.1) has no async or cancellation support"*, which is false, and wrote a plain `GH_Component` with a new Grasshopper package reference instead. With the skill, all three agents returned `Task<(double Mean, double StdDev)>` with a `CancellationToken`.

## The refactor finding (t5)

The refactor renames an input, adds an input and an output, and moves the subcategory.

- **Cost:** Betta without the skill is cheapest (0.78x tokens and 0.79x cost vs plain GH). With the skill it costs more (1.45x / 1.17x), because the agent loads the skill and then deals with GUID stability.
- **Saved files: this is the important part.** A Betta component's GUID is derived from its signature, parameter names included. So this refactor changes it, and existing `.gh` files show the component as missing.
  - The plain `GH_Component` keeps its hand-written `ComponentGuid`. All 3 plain runs stayed compatible by construction.
  - No-skill Betta agents never noticed (0 of 3).
  - Skill agents noticed and pinned a `Guid` (2 of 3). But they pinned a *new* one: the component's original GUID is `8b18f440-…`. So existing files break anyway.
- **Fixed in the skill:** it now says to pin the *current* GUID and where to read it. A tool or analyzer that supplies that value is on the roadmap. Agents can't compute an MD5 of a signature by hand.

## Limits

- One model, n = 3 per cell.
- Automated correctness checks: the build, and since run 4, whether a refactored component keeps its GUID. The async observations come from reading the generated code.
- Code tokens are chars/4 estimates. Use them as ratios.

## Run 1 (original skill) for reference

Betta ÷ plain over t1–t3: 0.44x code, 0.82x output tokens, but 1.27x total tokens and 1.47x cost. Loading the 21.5k-char skill put about 8.5k tokens and about 3 turns into every session. That finding led to the split skill measured in run 2.
