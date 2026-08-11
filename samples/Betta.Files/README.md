# Betta.Files

A **watch-first file toolkit** for Grasshopper — the pilot "colony" in the Betta
family. Every component below is generated from a plain attributed C# method;
there is **no `GH_Component` code**.

Its headline is a **clean, debounced live folder watch** — the thing Grasshopper
has never done well. Native `Read File` "Synchronise" only handles single files;
the DIY `GH_FileWatcher` fires a storm of recomputes per save. Betta.Files
coalesces that storm into **one** re-solve, and gives you the whole folder plus
what just changed.

## Files › Watch (streaming)

| Component | Does | Out |
|---|---|---|
| **Watch Folder** | Tracks a whole folder live. Emits a full snapshot on start, then re-emits on each settled change. | **AllPaths** (list) · **Changed** · **Kind** · **When** |
| **Watch File** | Streams a change each time one file settles (emits its current state on start). | Path · Kind · When |

`Watch Folder` returns `IObservable<FolderState>`; `Watch File` returns
`IObservable<FileChange>`. Betta subscribes, marshals each emission's re-solve to
the Rhino UI thread, and tears the watcher down when the component leaves the
canvas.

**How Watch Folder behaves:** on subscribe it emits every matching file in
`AllPaths` with `Changed`/`Kind`/`When` empty (the initial snapshot). On each
change it re-lists the folder and sets `Changed`/`Kind`/`When` to the file that
triggered it — so you always see the complete folder *and* the last change.

## Files › IO (read / write / find)

| Component | Does | Out |
|---|---|---|
| **Read Text** | Read a whole file as text. | text |
| **Read Lines** | Read a file into a list of lines. | list |
| **Split Delimited** | Split a delimited line into fields. Delimiter via **right-click menu** (Comma/Semicolon/Tab/Pipe). | fields |
| **Write Text** | Write text to a file (async). | written path |
| **Write Lines** | Write a list of lines to a file (async). | written path |
| **Find Files** | List files in a folder matching a glob. | paths |

## Try it

1. Build + deploy (post-build copies the DLL into the Betta plugin folder):
   ```bash
   dotnet build samples/Betta.Files/Betta.Files.csproj -c Debug
   ```
2. Restart Rhino/Grasshopper. Look under the **Files** tab (**Watch** and **IO** groups).
3. Drop **Watch Folder**, feed it a folder path (a Panel works), leave `Filter`
   at `*.*`. It immediately lists the folder in **AllPaths**.
4. Save a file in that folder from any app → the component re-solves; **Changed**
   shows the file, **AllPaths** updates. Wire **Changed → Read Text** to see the
   new contents live.
5. Right-click **Split Delimited** to switch the delimiter.

## Build notes

- **Pure logic, no Grasshopper/Rhino references** — services stay
  framework-agnostic; `Betta.gha` supplies the abstractions at load time.
- Abstractions is a project reference here so the pilot builds from the repo
  without packing. In a standalone colony repo, swap it for the
  `Betta.Abstractions` NuGet package (`ExcludeAssets="runtime"`).

See [DESIGN.md](DESIGN.md) for the gap analysis and design rationale.
