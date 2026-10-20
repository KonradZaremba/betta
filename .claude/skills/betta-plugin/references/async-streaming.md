# Async, cancellation, progress, streaming

## Async essentials (CancellationToken, IProgress)

Methods returning `Task<T>` (or `ValueTask<T>`) are async: the runtime kicks off the task, caches by input hash, and calls `ExpireSolution(true)` on completion. They can take a `CancellationToken` parameter. The runtime synthesizes one per solve and cancels the previous when the component re-solves, so stale work quits cleanly:

```csharp
[GrasshopperMethod("Slow Sum")]
async Task<double> SumAsync(List<double> xs, CancellationToken ct) { ... }
```

`IProgress<int>` / `IProgress<string>` parameters become a sink that updates the component's `Message` tag, giving users a "computing 42%" indicator for free. Both `CancellationToken` and `IProgress<T>` are synthetic: no input pin.

## Streaming (`IObservable<T>`)

A method returning `IObservable<T>` becomes a live component. `T` maps to outputs like a normal return (primitive, tuple, opaque, ...).

- Betta **subscribes on the first solve** (status shows *listening*); the method runs once per subscription, not once per emission.
- Every emission pushes the **newest value** to the output; re-solves are coalesced on the UI thread, so a fast stream can skip intermediate values. Not for lossless event delivery.
- **Changing any wired input** disposes the old subscription and re-invokes the method for a new one.
- The subscription is disposed when the component leaves the canvas; the `IDisposable` your observable's `Subscribe` returns must really release watchers/timers/sockets.

```csharp
[GrasshopperMethod("Ticker")]
public IObservable<long> Ticker([GrasshopperParameter("Seconds", DefaultValue = 1)] int seconds)
    => Observable.Interval(TimeSpan.FromSeconds(seconds));
```

Betta ships no Rx: using `System.Reactive` means bundling `System.Reactive.dll` next to your plugin DLL. Without it, hand-roll the observable like the `Ticker` in `Betta/Services/SimpleService.cs`.
