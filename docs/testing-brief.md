# Testing Brief — Closing the Betta solve-pipeline gap

> **Scope:** Betta runtime + NanoBanana · **Status:** Proposed · **Effort:** Phased (Tier 1 first) · **Owner:** betta repo

Four runtime bugs shipped past a green test suite because every existing test
exercises the plugin's *logic*, and none exercises the framework's **live
Grasshopper solve pipeline** — where all four lived. This is the plan to cover
that layer.

## 1 · The gap

The 5 existing NanoBanana tests verify *ingredients* — config parsing, seed
handling, the history store, DI registration. They call the service methods
directly, so they never run **through** Betta's component wrapper. Yet all four
bugs found this cycle are emergent behaviours of that wrapper across repeated
live solves — invisible to any headless, single-call test.

| # | Bug | Layer |
|---|-----|-------|
| 1 | Collectible-ALC poisoning (`0x80131515`) | dependency load |
| 2 | List / opaque outputs render as the type name | output emission |
| 3 | Unwired optional input blocks the solve | input guard |
| 4 | Async livelock + stale-image memoization | async cache |

## 2 · Three tiers, cheapest first

### Tier 1 — Pure-logic unit tests · headless · ~hours
Two of the four bugs were **pure functions**. Make them `internal` +
`InternalsVisibleTo` and pin them:

- `AsyncKey` stability — synthetic `CancellationToken` / `IProgress` must **not**
  change the key (this is the livelock).
- `PluginClassifier.IsPluginDll` against fixture DLLs.
- `OutputPlanner` output shaping.
- **Enabling refactor:** extract the async cache lifecycle out of
  `TryGetAsyncResult` into a testable `AsyncResultCache` (store / get /
  evict-on-delivery).

*Catches:* #4 (livelock + memoization), #1 (classification), #2 (planning).
*Needs:* nothing — plain xUnit.

### Tier 2 — Headless load / contract test · CI · ~1 day
Reuse the `betta-mcp` reflection-only loader (`MetadataLoadContext`, no Rhino):
scan the plugin folder and assert **"19 NanoBanana components, expected
signatures, zero load errors."** Guards discovery, registration and
dependency-version skew on any CI runner without a Rhino licence.

*Catches:* #1, #2 registration & skew regressions. *Needs:* CI only.

### Tier 3 — In-Rhino integration · Rhino on runner · higher lift
The only layer that runs the real solve pipeline. Using `Rhino.Testing`
(xUnit inside Rhino) with a **mocked** `IImageGenerationService`:

- solve → toggle Run → solve; assert the service fired **twice** (recompute).
- solve with an unwired optional input; assert no throw.
- watch the solve-count settle (no spin / livelock).
- load both plugins; assert the `BananaImage` wire connects.

*Catches:* all four — end to end. *Needs:* Rhino 8 on the runner.

## 3 · Coverage matrix

| Bug | Tier 1 | Tier 2 | Tier 3 |
|-----|:------:|:------:|:------:|
| #1 Collectible-ALC poisoning | ● | ● | ● |
| #2 List / opaque outputs | ● | ◐ | ● |
| #3 Unwired optional input | ◐ | — | ● |
| #4 Async livelock + memoization | ● | — | ● |

`●` caught · `◐` partial · `—` out of scope

## 4 · Rollout & definition of done

- **Phase 1** — Tier 1 + the `AsyncResultCache` extraction. Guards the two bugs
  that actually cost us this cycle. No Rhino, mergeable immediately.
- **Phase 2** — Tier 2 contract test wired into CI. Every push verifies the
  component set loads clean.
- **Phase 3** — Tier 3 harness + one regression test per bug. NanoBanana also
  gets a single smoke test: 19 components load & solve, Generate calls the API
  each Run.

**Standing rules**

- Every bug fix ships with a test that fails without it.
- A method that *looks pure but has a side effect* (image gen, API call) gets an
  "invoked each call" assertion.
- Framework-level tests live in the **betta repo** — they protect every plugin,
  not just this one.
