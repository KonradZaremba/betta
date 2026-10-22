# Use case: a facade panel library, vibe-coded two ways

One brief, an 8-component Grasshopper plugin for facade panel fabrication, handed to a coding agent in an empty project. It was built three times as plain hand-written `GH_Component`s and three times on Betta. This folder keeps the best library of each kind, exactly as the agent wrote it:

- [`regular/`](regular): hand-written `GH_Component`s (`FacadePanels.Regular.gha`, standalone)
- [`betta/`](betta): the same library on Betta (`FacadePanels.Betta.dll`, needs the Betta plugin in Rhino)
- [`brief.md`](brief.md): the prompt both got, word for word

**The library:** Panelize Surface, Panel Info, Planarity, Classify Panels, Panel Stats, Group Identical Panels, Nest On Sheets and Cut List. A `Panel` object travels between components on its own wire. Nesting runs in the background, cancels when inputs change and shows progress. Bad input gives errors; warnings still produce outputs. Cut List writes a file only when asked to. These are the parts of a real plugin that are tedious to write by hand.

## Results

Claude Code (Claude Sonnet 5.5), headless, one session per build, isolated from user settings. Medians of 3 sessions per setup.

| | Hand-written `GH_Component` | Betta, template guide | Betta, guide + skill + Betta.Mcp |
|---|---|---|---|
| Cost per library | $0.41 | $0.28 (**0.67x**) | $0.22 (**0.54x**) |
| Wall time | 3.0 min | 1.5 min | 1.5 min |
| Code written | 698 lines | 357 lines (0.51x) | 327 lines (**0.47x**) |
| Output tokens | 24.6k | 10.2k | 9.8k |
| Builds | 3/3 | 3/3 | 3/3 |
| All 8 components, every input and output the brief names, typed Panel wire | 3/3 | 3/3 | 3/3 |
| Code review, mean of 30 | 24.7 | 26.3 | **27.3** |

The libraries kept here: `regular/` is 605 lines in 5 files, `betta/` is 300 lines in 3 files.

**How the checks work:**
- **Structure:** a checker loads each build headlessly, the way Grasshopper builds components, and compares every component, input and output with the brief.
- **Review:** a separate Opus reviewer read each library against the brief with one rubric of six scores from 0 to 5 (completeness, correctness, errors and warnings, background work, Grasshopper hygiene, maintainability).

| Review (mean of 3) | Hand-written | Betta, guide | Betta, guide + skill + MCP |
|---|---|---|---|
| Completeness | 5.0 | 5.0 | 5.0 |
| Correctness | 4.0 | 4.0 | 4.0 |
| Errors and warnings | 4.0 | 4.3 | 4.7 |
| Background nesting | **3.3** | 4.0 | **4.7** |
| Grasshopper hygiene | 4.3 | 4.7 | 4.3 |
| Maintainability | 4.0 | 4.3 | 4.7 |

The geometry and math came out essentially right on every side: correctness was 4 of 5 everywhere, for small judgement calls such as when to turn a part. The gap is in the plumbing around them, which Betta writes for you.

## Where the difference comes from

### A component is a method

The hand-written Planarity component (`regular/Facade/Components.cs`, 39 lines; the `Panel` input helpers live in a shared base class):

```csharp
public class PlanarityComponent : FacadeComponent
{
    public PlanarityComponent()
        : base("Planarity", "Planarity", "Measures how flat each panel is", "Analysis") { }

    public override Guid ComponentGuid => new Guid("3f8b5c32-4e6f-4aa7-89b3-2fb5d7e4a013");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        AddPanelInput(pManager, "Panels", "P", "Panels", GH_ParamAccess.list);
        pManager.AddNumberParameter("Tolerance", "T", "Maximum deviation for a flat panel", GH_ParamAccess.item, 0.5);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Deviation", "D", "Planarity deviation per panel", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Flat", "F", "True when deviation is within tolerance", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Flat Count", "N", "Number of flat panels", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var panels = GetPanels(DA, 0);
        double tol = 0.5;
        if (panels == null) return;
        if (!DA.GetData(1, ref tol)) return;
        // ... the same 7 lines of logic as below, then DA.SetDataList / DA.SetData for each output
    }
}
```

The Betta one (`betta/Facade/IFacadePack.cs` and `FacadePack.cs`):

```csharp
[GrasshopperMethod("Planarity", "Planarity deviation of each panel", SubCategory = "Analysis")]
(List<double> Deviation, List<bool> Flat, int FlatCount) Planarity(
    [GrasshopperParameter("Panels")] List<Panel> panels,
    [GrasshopperParameter("Tolerance", DefaultValue = 0.5)] double tolerance,
    IBettaReport report);

public (List<double> Deviation, List<bool> Flat, int FlatCount) Planarity(
    List<Panel> panels, double tolerance, IBettaReport report)
{
    var deviation = panels.Select(p => p.Deviation).ToList();
    var flat = deviation.Select(d => d <= tolerance).ToList();
    int flatCount = flat.Count(f => f);
    int notFlat = panels.Count - flatCount;
    if (notFlat * 2 > panels.Count)
        report.Warning($"{notFlat} of {panels.Count} panels are not flat.");
    return (deviation, flat, flatCount);
}
```

Inputs, outputs, list or item access, the warning and the component GUID all follow from the signature. It's plain C#, so it can be unit-tested without Grasshopper.

### The Panel wire

To carry `Panel` objects on a typed wire, the hand-written library needs a `GH_Goo<Panel>` wrapper, a `GH_Param<GH_Panel>` parameter with its own GUID, and helpers to read and write them in every component (`regular/Facade/PanelParam.cs`, `FacadeComponent.cs`). The Betta library puts one attribute on the class:

```csharp
[GrasshopperOpaque]
public class Panel { ... }
```

### Background work

Nest On Sheets has to run off the UI thread, drop the old run when inputs change and show progress.

- **Hand-written** (`regular/Facade/NestOnSheetsComponent.cs`, 215 lines): about 120 of them are plumbing. There's an input key to tell whether a run is current, a result cache, a lock, a `CancellationTokenSource`, `Task.Run`, `RhinoApp.InvokeOnUiThread`, `ExpireSolution` to show the result, progress through `Message`, and cleanup in `RemovedFromDocument`.
- **Betta**: the signature is the plumbing.

```csharp
Task<(List<Curve> Outlines, List<int> Sheet, int SheetCount, double Utilization)> NestOnSheets(
    List<Panel> panels, double sheetWidth, double sheetHeight, double spacing,
    CancellationToken cancellationToken, IProgress<int> progress, IBettaReport report);
```

This is where the reviewers found the hand-written versions' real problems:
- In 2 of 3, an exception other than cancellation leaves the component stuck on "Nesting n%" with no error.
- In 2 of 3, the status message is updated from a worker thread.
- In the library kept here, any failure becomes a misleading "N panels did not fit" warning.

### Errors

In Betta, throwing an exception shows the error and sets no outputs. Hand-written components have to call `AddRuntimeMessage` and remember to `return` before setting outputs. In all three hand-written libraries, Cut List with Write on and no Path shows the error but still outputs the CSV.

## What to keep in mind

- **One model, one brief, 3 sessions per setup.** The cost and size ratios matched the earlier, smaller tasks in [`bench/tokens/RESULTS.md`](../../bench/tokens/RESULTS.md) (about 0.8x cost, 0.6x code, 0.5x for five components at once). This library sits at the larger-savings end of that range.
- **Checked by loading and reading, not yet in Rhino.** Every build was loaded headlessly and every component inspected. Correctness comes from the reviews, not from running the components on a facade.
- **Betta.Mcp wasn't used.** The "+ MCP" sessions never called it for a new library. The gain over the template guide alone comes from the skill: those agents read its pages on opaque types and async, while guide-only agents went digging through the NuGet package for the same facts.
- **Output names.** Betta takes output names from named tuple elements, which can't contain spaces. When these libraries were built, the Betta ones showed "FlatCount" and "SheetCount" where the brief said "Flat Count" and "Sheet Count"; the structure scores above are from then. Betta 0.8 now splits such names into words, so the same code shows "Flat Count".
- **Betta needs Betta.** The regular `.gha` stands alone; the Betta DLL needs the Betta plugin (0.8) installed in Rhino.
- **Prerelease.** Built against Betta 0.8 before release. Until Betta.Abstractions 0.8.0 is on nuget.org, `betta/` references the SDK project in this repo.

## Build and try

```powershell
dotnet build usecases/facade-panels/regular -c Release   # copy FacadePanels.Regular.gha to %AppData%\Grasshopper\Libraries\
dotnet build usecases/facade-panels/betta -c Release     # copy FacadePanels.Betta.dll to %AppData%\Grasshopper\Libraries\Betta\
```

## Reproduce

The sessions, scores and reviews come from the token benchmark harness (`bench/tokens`). Run the sessions, then score the builds:

```powershell
powershell -File bench/tokens/run.ps1 -Tasks u1-facade -Arms betta-guide-skill-mcp,vanilla,betta-guide -Runs 3 -MaxTurns 80 -MaxBudgetUsd 8 -Trace -AbstractionsVersion <0.8 package> -OutDir bench/tokens/results/usecase-facade
dotnet build bench/tokens/inspect -c Release
powershell -File bench/tokens/score-usecase.ps1 -OutDir bench/tokens/results/usecase-facade
```

Raw results, per-session transcripts, the structure check's JSON and all nine reviews ([`reviews.md`](../../bench/tokens/results/usecase-facade/reviews.md)) are in [`bench/tokens/results/usecase-facade`](../../bench/tokens/results/usecase-facade).
