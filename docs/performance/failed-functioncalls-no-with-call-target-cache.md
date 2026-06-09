# Failed functioncalls no-with call-target cache

## Selection

The required fresh full benchmark table still selected `functioncalls` on
current `origin/main`. It had the largest absolute Asynkron-side loss in the
table after the retained plan-dependency and production-eligibility caches:

```text
profile                    asynkron_ms  jint_ms  delta
functioncalls                     4643     2179  Jint 2.13x faster
```

Baseline signal: `functioncalls` full-table Asynkron row = 4643 ms.

An active sibling issue, `agentmanual1780998418927155000`, was already scoped
to reprofile the #3547 `SyncFunctionInvoker` eligibility cache impact. This run
therefore avoided a duplicate evidence-only update for that cache and looked
only for a new narrow runtime slice.

## Profile owner

The requested CPU profile was run three times sequentially:

```bash
rtk ./tools/profile functioncalls --cpu --calltree-depth 40 --calltree-width 40
```

Two attempted parallel profile captures failed trace conversion with:

```text
Speedscope conversion failed: System.FormatException: Failed to read byte[191998276] at stream offset 0x675c
```

Those captures were discarded as profiler artifact collisions and replaced by
sequential runs.

The successful profiles no longer showed the investigated
`SyncFunctionInvoker` call-environment scan as a hot owner. The repeated
current owner was script production unified-bytecode execution with a smaller
dynamic identifier call-target residual:

```text
UnifiedBytecodeVirtualMachine.Execute
TypedAstEvaluator.TryRunScriptViaProductionUnifiedBytecode
UnifiedBytecodeVirtualMachine.PrepareDynamicIdentifierCallTarget
JsEnvironment.TryGetIdentifierJsValueAfterWithMiss
Dictionary<Symbol, JsEnvironment.ResolvedIdentifierBinding>.Resize
```

The `PrepareDynamicIdentifierCallTarget` rows were 333 ms, 363 ms, and 383 ms
inside about 6498-6512 ms profile totals. That is below the documented 10%
retry gate for the previously failed dynamic call-target symbol cache, so this
run did not retry symbol/name interning.

## Trial

The trial kept to the observed owner without changing dynamic symbol storage:

- `PrepareDynamicIdentifierCallTarget` used the existing `AllowIdentifierCache`
  no-with contract to skip the per-call `HasWithObjectInChain` branch when the
  context already guarantees identifier caching is safe.
- `JsEnvironment` added a no-with identifier lookup helper for that path.
- The identifier binding cache was initialized with a small fixed capacity to
  avoid the sampled dictionary-resize subtree during first population.

The edit built cleanly:

```text
rtk dotnet build -c Release
ok dotnet build: 11 projects, 0 errors, 7 warnings
```

Focused trial rows were:

```text
functioncalls                  4636     2150  Jint 2.16x faster
functioncalls                  4638     2131  Jint 2.18x faster
functioncalls                  4850     2153  Jint 2.25x faster
```

Final signal: `functioncalls` trial Asynkron focused rows = 4636, 4638, 4850 ms
(average 4708 ms).

Signal delta: 4643 ms -> 4708 ms, 65 ms slower, about 1.4% regression versus
the selected full-table baseline row.

## Outcome

The trial missed the required 10% retained-performance threshold and did not
produce a stable improvement, so the runtime edit was reverted. No source
optimization remains from this attempt.

The result is still useful: skipping the no-with branch and pre-sizing the
identifier binding cache are not enough to move the current `functioncalls`
benchmark after #3547. Future work should not retry this exact shape unless a
fresh profile shows `HasWithObjectInChain` or identifier binding cache resizing
as a larger repeated owner. The remaining larger cost is still broad production
unified-bytecode script execution plus dynamic call-target lookup.

## Commands run

```bash
rtk ./benchmark.sh
rtk ./tools/profile functioncalls --cpu --calltree-depth 40 --calltree-width 40
rtk ./tools/profile functioncalls --cpu --calltree-depth 40 --calltree-width 40
rtk ./tools/profile functioncalls --cpu --calltree-depth 40 --calltree-width 40
rtk dotnet build -c Release
rtk ./benchmark.sh --no-build functioncalls
rtk ./benchmark.sh --no-build functioncalls
rtk ./benchmark.sh --no-build functioncalls
```
