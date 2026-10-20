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
