# Trees, branched inputs, validation

## Tree inputs and outputs

`GH_Structure<TGoo>` parameters and returns flow as trees: the method body sees the whole structure intact rather than per-iteration items:

```csharp
[GrasshopperMethod("Batch From Tree")]
List<Graph> BatchFromTree(
    [GrasshopperParameter("Seeds")] GH_Structure<GH_Number> seeds,
    [GrasshopperParameter("Kind")] GraphKind kind);
```

Item/list inputs in the same method continue to iterate normally; trees co-exist with both.

## Branched inputs

Item and list inputs wired with multiple branches are flattened automatically (no per-branch mismatch). To see the branch structure, take `GH_Structure<TGoo>`.

## Param validation

Gate parameter values before the method body runs:

| Attribute | Behavior |
|---|---|
| `[GrasshopperRange(min, max)]` | Numeric range; out-of-range surfaces a Warning and skips invocation. |
| `[GrasshopperNotEmpty]` | Rejects null/whitespace strings and empty collections. |
| `[GrasshopperValidation(typeof(MyValidator))]` | Custom rule: `IBettaValidator.Validate(value)` returns null on success or the warning message on failure. |

The component continues solving the next branch; only the offending invocation is skipped.

## Errors and warnings from the method body

Validators run before the method and skip it. For anything found while computing:

- **Error, no outputs:** throw. Betta catches it and shows `Method execution failed: <message>` as an error; the component outputs nothing for that call.
- **Warning, outputs still set:** take an `IBettaReport` parameter (synthetic, no input pin) and call `report.Warning("…")` or `report.Remark("…")`. Betta shows the messages when it outputs the result: for async methods when the task's result arrives, and for streams everything reported since the subscription opened, on every update. It is thread-safe, identical messages show once, and at most 50 are kept.

```csharp
[GrasshopperMethod("Shelf Pack", "Packs parts onto sheets")]
(List<Curve> Outlines, List<int> Unplaced) Pack(List<double> widths, List<double> heights, IBettaReport report)
{
    if (widths.Count != heights.Count) throw new ArgumentException("Widths and Heights differ in length");
    // ... pack ...
    if (unplaced.Count > 0) report.Warning($"{unplaced.Count} parts didn't fit");
    return (outlines, unplaced);
}
```
