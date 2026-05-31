# Failed Activation Arguments PushEnvironment Slot Names

Date: 2026-05-31

## Selected Profile

`activation-arguments-lite` stayed within the recurrence run's investigated
slice because the fresh matrix still showed a clear activation-arguments gap:

```text
activation-arguments-lite          694      271  Jint 2.56x faster
```

Repeated focused baseline rows before editing were:

```text
activation-arguments-lite          628      269  Jint 2.33x faster
activation-arguments-lite          641      351  Jint 1.83x faster
activation-arguments-lite          629      274  Jint 2.30x faster
```

Baseline timestamp: 2026-05-31T23:12:25Z
Baseline signal: activation-arguments-lite Asynkron focused average = 632.7 ms

## Profile Finding

The required CPU profile command was run three times before editing:

```bash
rtk ./tools/profile activation-arguments-lite --cpu --calltree-depth 40 --calltree-width 40
```

The direct `arguments[i]` read path was no longer the dominant sampled owner.
The repeated call trees instead put the largest residual activation cost under
`HandlePushEnvironment`, `JsEnvironment.SetSlotMap`, and
`CreateArgumentsObject` / `JsArgumentsObject.ctor`. In the first profile, direct
computed property reads through `TryGetArrayLikeValueJsValue` were only a small
subtree under `HandleCompoundAssignmentSlot`.

## Reverted Experiment

I tried carrying precomputed `SlotNames` payloads on `PushEnvironmentInstruction`
instances emitted by block-scope and block-function-declaration emitters that
still supplied only `SlotMap`. The intended win was to move the
`ImmutableDictionary` slot-map enumeration out of the runtime activation path
and let `HandlePushEnvironment` call `SetSlotNames`.

The change built cleanly:

```bash
rtk dotnet build -c Release src/Asynkron.JsEngine/Asynkron.JsEngine.csproj
```

Focused rows with the experiment retained were slower than the baseline:

```text
activation-arguments-lite          668      312  Jint 2.14x faster
activation-arguments-lite          665      250  Jint 2.66x faster
activation-arguments-lite          663      288  Jint 2.30x faster
```

Final timestamp: 2026-05-31T23:16:47Z
Final signal: activation-arguments-lite Asynkron focused average = 665.3 ms
Signal delta: +32.6 ms, 5.2% slower

Because the experiment missed the required 10% improvement and regressed the
selected rows, the runtime edit was reverted. No production runtime code from
this attempt is retained.

## Follow-Up

Do not retry generic block/control-emitter `SlotNames` payload widening for this
profile without first proving that the specific `PushEnvironment` instances in
`activation-arguments-lite` consume the widened payload. The sampled
`SetSlotMap` cost may be from activation or loop-scope shapes that already need
a more targeted owner than this broad emitter-level payload change.
