# Menu state, secrets, trigger, value list

Only value list leaves a wired input pin; the others are not wired inputs.

## Per-instance menu state (right-click submenu, not wired input)

Tag a parameter `[GrasshopperMenuState]` and the user picks the value once from the component's right-click menu. The choice persists with the .gh. v0.5 ships UI for `enum` (sub-menu of all members) and `bool` (toggle); other types accept the persisted value but show no editor yet.

```csharp
[GrasshopperMethod("Render")]
Bitmap Render(
    [GrasshopperParameter("Quality"), GrasshopperMenuState] Quality q,
    [GrasshopperParameter("Scene")] string scene);

public enum Quality { Draft, Final }
```

## Other parameter attributes

- `[GrasshopperSecret("service.key")]`: value read from the OS credential store, not a wired input; a missing value Warns + skips.
- `[GrasshopperTrigger]` on `bool`: no input pin; the component adds a "Run" menu item and only fires when clicked.
- `[GrasshopperValueList(items...)]`: a regular input pin, but Betta auto-drops a wired `GH_ValueList` with those items when the component is placed.
