# Token benchmark results — 2026-10-21

A headless Claude Code agent (Claude Sonnet 5.5) added the same Grasshopper component to two fresh projects. One is a Betta plugin with the `betta-plugin` skill. The other is a plain plugin with one example `GH_Component`. There were 3 tasks, 3 runs each per project, and every run was isolated from user settings. Full numbers: [results/2026-10-21-0005/report.md](results/2026-10-21-0005/report.md). Context-cost calibration: [results/calibration/calibration.md](results/calibration/calibration.md).

## Headline

| | Betta / plain (median, all tasks) |
|---|---|
| Builds | 18 / 18 pass (9 each) |
| Code the agent wrote | **0.44x** (56% less) |
| Output tokens | **0.82x** |
| Wall time | **0.82x** |
| Total session tokens | 1.27x |
| Cost | 1.47x |
| Turns | 1.53x (9 vs 6) |

**For one small component in a fresh session, Betta makes the result cheaper to review but not cheaper to generate.** The agent writes less than half the code and fewer output tokens, and it finishes sooner. But the whole session costs about 1.3x the tokens and 1.5x the dollars.

## Why the session costs more

The extra cost is the skill. The Betta agent invokes `betta-plugin` first, which puts about 8.5k tokens into the context: the final context is about 47k vs 38–40k. It also spends about 3 extra turns invoking and reading it. Every later turn re-reads that larger context. So on a component that takes a few dozen lines, this fixed cost outweighs the boilerplate Betta saves.

The calibration bears this out. The installed skill costs about 250 tokens while unused and about 8.6k once loaded. Betta.Mcp costs about 180 tokens connected and about 1.3k once its schemas load. It wasn't used in these runs.

## Quality notes

- **Async (t3):** all three Betta agents returned `Task<(double Mean, double StdDev)>` with a `CancellationToken`, and the runtime does the threading. None of the three plain agents used `GH_TaskCapableComponent`. Each hand-rolled `Task.Run` → `ScheduleSolution` → `ExpireSolution` with a `CancellationTokenSource`. That's a reasonable pattern, but about 3x the code (898 vs 299 est. tokens).
- **Code size by hand:** the hand-written reference versions show the same ratio, 0.47x.

## Limits

- One model, n = 3 per cell, one component per session.
- "Builds" is the only correctness check: nothing ran inside Grasshopper.
- Code tokens are chars/4 estimates. The calibration shows chars/4 undercounts markdown and code, so read them as ratios.
- The README's specific claim, that *refactors* don't re-spend tokens on boilerplate, isn't tested here.

## Next experiments

1. **Betta without the skill:** add the `-SkillMode none` arm (9 sessions). It separates the skill's fixed cost from Betta's own effect.
2. **Several components per session:** one prompt asking for 5 components. This tests whether the fixed skill cost spreads out over more work.
3. **Refactor task:** rename a parameter, add an output, change a return type. This is the README's actual claim.
4. **A leaner skill:** keep a short core in `SKILL.md` and move the reference sections to files the agent reads only when needed. This cuts the ~8.5k loaded cost for the common case.
