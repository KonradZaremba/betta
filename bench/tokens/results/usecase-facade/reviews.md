# Code reviews: facade use case

One fresh Opus reviewer per library, read-only, with the same prompt: judge the library against `tasks/u1-facade.md` only, score six dimensions 0-5 and list bugs with file:line. Reviewers were told how Betta's attributes, `Task`/`CancellationToken`, `IProgress`, `IBettaReport` and exceptions behave. The scaffold's sample components were excluded.

| Library | Completeness | Correctness | Errors | Background | Hygiene | Maintainability | Total /30 |
|---|---|---|---|---|---|---|---|
| betta-guide-skill-mcp-1 | 5 | 4 | 4 | 4 | 5 | 5 | 27 |
| betta-guide-skill-mcp-2 | 5 | 4 | 5 | 5 | 4 | 4 | 27 |
| betta-guide-skill-mcp-3 (kept in `usecases/facade-panels/betta`) | 5 | 4 | 5 | 5 | 4 | 5 | **28** |
| betta-guide-1 | 5 | 4 | 4 | 4 | 5 | 4 | 26 |
| betta-guide-2 | 5 | 4 | 4 | 4 | 4 | 5 | 26 |
| betta-guide-3 | 5 | 4 | 5 | 4 | 5 | 4 | 27 |
| vanilla-1 | 5 | 4 | 4 | 3 | 4 | 4 | 24 |
| vanilla-2 (kept in `usecases/facade-panels/regular`) | 5 | 4 | 4 | 4 | 5 | 4 | **26** |
| vanilla-3 | 5 | 4 | 4 | 3 | 4 | 4 | 24 |

## Findings that recur

**Hand-written (`vanilla`), all in the hand-rolled background nesting or error handling:**
- An exception other than cancellation leaves Nest On Sheets stuck on "Nesting n%" with no error (vanilla-1, vanilla-3); vanilla-2 turns any failure into a misleading "N panels did not fit" warning.
- `Message` set (and the canvas redrawn) from the worker thread (vanilla-1, vanilla-3); progress written without the lock (vanilla-2).
- Cached results consumed once or shown against stale inputs after a failed input read (vanilla-1, vanilla-3).
- Cut List with Write on and an empty Path shows the error but still outputs the CSV (all three).
- `PanelGoo` without `CastFrom`/`CastTo` or persistence (vanilla-1; not required by the brief).

**Betta (both setups):**
- `report.Warning` called inside the `Task.Run` worker (flagged by two reviewers as a possible thread issue; `IBettaReport` is thread-safe and Betta posts its messages after the result, so this is safe).
- Shelf nesting judgement calls: a part turned to fit the current shelf before a new shelf is tried; sorting by unrotated height (betta-guide-2, betta-guide-skill-mcp-3). Valid packings, slightly less efficient.
- Sheet Width/Height/Spacing not validated (betta-guide-1, vanilla-2); missing directories in Cut List's path surface as an error (several, acceptable).
- Grid index order (U- or V-fastest) and 0-based sheet index: the brief leaves both open.

No reviewer found a geometry or math bug in any library: grid corners, the deviation definition, classification thresholds, sorted-edge/diagonal grouping with first-appearance numbering, rectangle sizes, sheet layout, utilization and CSV columns matched the brief everywhere.
