# Betta.Files — Design

**Status:** pilot built + headlessly verified (16/16 tests). 2026-08-11.

## Context

Betta ships plenty of *tutorial* content (Quickstart, Tour, OpaqueDemo, Strings) —
toy math/geometry that teaches the framework. None of it is a toolkit a
Grasshopper user would install to get work done. Betta.Files is the first of a
planned **family of small, independently downloadable "colonies"**, each solving
one real GH pain and doubling as a flagship "look what ~250 lines of attributed
C# does" showcase.

## The gap (researched)

File I/O is a chronic GH friction point:

- **Folder-watching is unmet.** Users ask [how to watch a *folder*](https://discourse.mcneel.com/t/how-to-watch-synchronise-a-folder/73822);
  native `Read File` "Synchronise" only does single files. The DIY
  [`GH_FileWatcher` fires multiple recomputes](https://discourse.mcneel.com/t/gh-filewatcher-multiple-recomputes/149813)
  per save — a known, unsolved annoyance.
- **Incumbents are clunky/buggy** (TT Toolbox batch-overwrite defect; LunchBox
  Read CSV is basic), and CSV/file friction is constant on the forums.

**Bullseye:** a clean, debounced live folder watch — simultaneously the sharpest
gap and the perfect `IObservable` streaming showcase.

## Scope

Two collections under one **Files** category:

**Files › Watch** ([`FileWatchers`](FileWatchers.cs)) — the streaming half:
- **Watch Folder** → `IObservable<FolderState>`
- **Watch File** → `IObservable<FileChange>`

**Files › IO** ([`FileToolkit`](FileToolkit.cs)) — read/write/find:
- Read Text, Read Lines, Split Delimited (menu-state delimiter)
- Write Text, Write Lines (async)
- Find Files (glob)

The streaming and IO halves are deliberately separate collections so the live
watchers sit apart from the plain utilities.

## Watch Folder behaviour (the key decision)

A component's output pins are fixed at creation, so instead of a mode switch,
Watch Folder emits a single rich shape that carries **both** the whole folder and
the last change. [`FolderState`](FolderState.cs) is a plain class → Betta explodes
it into four pins:

- **AllPaths** (list) — every matching file currently in the folder
- **Changed** / **Kind** / **When** (scalars) — the file that triggered this
  emission (empty on the initial snapshot)

On subscribe it emits the full snapshot (AllPaths populated, Changed empty). On
each settled change it re-lists the folder and fills Changed/Kind/When. So
downstream always sees the complete folder *and* what just changed — not only the
last changed file.

## The debounced watcher

[`DebouncedFolderWatch`](DebouncedFolderWatch.cs) wraps `FileSystemWatcher` and
coalesces the burst a single save produces, per path, into one
`onSettled(path, kind)` after a quiet window (default 250 ms) — the fix for
GH_FileWatcher's multiple-recompute problem. Both observables
([`WatchObservables.cs`](WatchObservables.cs)) share it; each also fires a
deferred initial snapshot. `Dispose` tears everything down, which Betta calls
when inputs change or the component leaves the canvas.

## Betta features showcased

| Feature | Where |
|---|---|
| `IObservable<T>` streaming | Watch Folder / Watch File |
| Class-return explosion | `FolderState` → AllPaths[] + 3 scalars |
| `async` (`Task<T>` unwrap) | Write Text / Write Lines |
| Validation attributes | `[GrasshopperNotEmpty]` on path inputs |
| Menu-state | `[GrasshopperMenuState]` delimiter on Split Delimited |

## Isolation / structure

- `FileWatchers` / `FileToolkit` — the two collections.
- `DebouncedFolderWatch` — shared FSW + debounce (single responsibility).
- `FolderStateObservable` / `FileChangeObservable` — the two observables.
- `FolderState` / `FileChange` — result DTOs.

Pure logic, **no Grasshopper/Rhino references**. Buildable and testable without
Rhino.

## Testing

[`TestBetta/TestFileToolkit.cs`](../../TestBetta/TestFileToolkit.cs) — 16 fully
headless tests (dogfoods the `TestStreamingTicker` pattern):

- Discovery of both collections; Watch Folder is `IObservable<FolderState>`.
- `FolderState` explodes to `AllPaths[]` + three scalar pins (output-shape lock).
- Watch Folder: initial snapshot lists existing files; a change flags `Changed`
  and re-lists; missing folder still snapshots (empty) without throwing.
- Watch File emits its existing state on start.
- IO round-trips, all four delimiters, glob find, missing-file empty.

## Distribution

Built in-repo under `samples/Betta.Files/`. Lifts into a standalone downloadable
colony by swapping the Abstractions project reference for the
`Betta.Abstractions` NuGet package and adding its own `.zip`/`.yak` release flow.
The **template** for the rest of the family (Data, Web, Text, Time).
