# SimpleArithmetic IIFE Per-Call Overhead: NeedsArgumentsBinding and Eligibility Cache

Date: 2026-05-30
Issue: autrun-diwap9usj808-20f5ecd5ca

## Selected Profile

`simplearithmetic` was selected from the full `rtk ./benchmark.sh` baseline because it
was the largest current Asynkron-vs-Jint ratio loss:

```text
profile                    asynkron_ms  jint_ms  delta
simplearithmetic                   285       80  Jint 3.56x faster
```

Baseline timestamp: 2026-05-30T20:50:00Z
Baseline signal: `simplearithmetic` Asynkron (3-run average) = 286ms (runs: 275, 300, 284)

## CPU Profile Evidence

The required profile command:

```bash
rtk ./tools/profile simplearithmetic --cpu --calltree-depth 40 --calltree-width 40
```

### Profile 1 (initial)

```text
ExecutionPlanRunner.EnsureExecutionEnvironment (28.9%)
  CreateExecutionEnvironment (24.1%)
    NeedsArgumentsBinding / ArgumentsReferenceDetector.ContainsArgumentsReference (4.8%)
    CreateInvocationEnvironment / JsEnvironmentPool.Rent (2.4%)
    InitializeSlotsWithCapacity / JsSlotArrayPool.Rent (2.4%)
SyncFunctionInvoker.TryInvokeProductionUnifiedBytecode (7.1%)
  UnifiedBytecodeProductionEligibility.Evaluate (7.1%)
    TryBuildActiveWithDepths + TryFindExpressionDecline (5.0%)
CreateFunctionValue / SyncFunctionInvoker.ctor (9.7%)
ExecutePlan (19.1%)
HandleReturn / CloseActiveIterators (6.8%)
```

## Root Cause Analysis

The `simplearithmetic` benchmark wraps a small script in an IIFE and evaluates it 10,000 times
against a shared parsed program. Each iteration evaluates the `FunctionExpression` node to produce
a new `JsFunction` with a fresh `SyncFunctionInvoker`, then immediately calls it.

Because `SyncFunctionInvoker` is a new instance per call, two important caches were being reset:

### Hotspot 1: `NeedsArgumentsBinding` AST traversal in `CreateExecutionEnvironment`

`ExecutionPlanRunner.CreateExecutionEnvironment` called `NeedsArgumentsBinding(_function)` on every
invocation (line 31 of `TypedAstEvaluator.ExecutionPlanRunner.Environment.cs`). This walked the
function's AST body with `ArgumentsReferenceDetector.ContainsArgumentsReference` on every one of
the 10,000 iterations even though the result is purely determined by the immutable AST.

The same traversal appeared in `SyncFunctionInvoker.ctor` (lines 191-192) for `_usesArguments` and
`_needsArgumentsBinding`, also per IIFE evaluation.

### Hotspot 2: Production unified bytecode eligibility re-evaluated per IIFE call

`SyncFunctionInvoker.TryGetProductionUnifiedBytecodeProgram` cached the eligibility result per
`SyncFunctionInvoker` instance. Since a new instance was created on every IIFE iteration, the cached
result was discarded, forcing `UnifiedBytecodeProductionEligibility.Evaluate` to re-run on every
call. This ran `TryBuildActiveWithDepths` (an `Array.Fill` + instruction walk) and
`TryFindExpressionDecline` on every iteration.

For `simplearithmetic`, the function's execution plan always declines (the expression program has
`Math.sqrt(16)` and `Math.pow(2, 10)` calls that are not in the first boundary of the IR plan).
The decline is purely structural — it is identical on every call — yet 10,000 eligibility evaluations
were being paid.

## Changes

### Change 1: Add `UsesArguments` and `NeedsArgumentsBinding` to `FunctionInvokerStaticPlan`

`FunctionInvokerStaticPlan` already caches several pure-AST properties (parameter var declaration,
function declaration conflict, non-parameter callee call, inner function expression). `UsesArguments`
and `NeedsArgumentsBinding` are also pure-AST and were added:

- `FunctionInvokerStaticPlan.cs`: added `UsesArguments` and `NeedsArgumentsBinding` bool properties
  and computed them in `Build`.
- `SyncFunctionInvoker.ctor` (lines 191-192): `UsesArgumentsIdentifier` and `NeedsArgumentsBinding`
  direct calls replaced with `invokerStatics.UsesArguments` and `invokerStatics.NeedsArgumentsBinding`.
  The `invokerStatics` retrieval was moved earlier in the constructor to serve both the existing
  and new uses.
- `ExecutionPlanRunner.CreateExecutionEnvironment` (line 31): the direct `NeedsArgumentsBinding(_function)`
  call replaced with `invokerStatics.NeedsArgumentsBinding` read from the same plan-cached data.

### Change 2: Cache plan-level eligibility decline on `ExecutionPlan`

Added `_productionEligibilityPermanentDecline: volatile bool` to `ExecutionPlan` (a `sealed record`)
with accessor methods `IsProductionEligibilityPermanentDecline` and
`MarkProductionEligibilityPermanentDecline`.

In `SyncFunctionInvoker.TryGetProductionUnifiedBytecodeProgram`:
- New fast path: if `plan.IsProductionEligibilityPermanentDecline`, return false immediately without
  calling `Evaluate`.
- After `Evaluate` returns a decline, call `plan.MarkProductionEligibilityPermanentDecline()` when
  `IsPlanStructuralDecline(result.Code)` is true.

`IsPlanStructuralDecline` classifies a decline as "structural" (safe to cache on the plan for all
future invocations) when the code is NOT one of the descriptor-dependent codes:
`AsyncLikeFunction`, `GeneratorFunction`, `CapturedOrDynamicActivation`,
`ArgumentsObjectDependency`, `ThisDependency`, `NewTargetDependency`.

After `CanUseProductionUnifiedBytecodeFastPath` has returned `true`:
- `DynamicLookupDependency` is unreachable at the descriptor level (the fast-path guard `!_allowIdentifierCache && !canUseDynamicNamePath` is one of the early-return conditions). Any `DynamicLookupDependency` from `Evaluate` is plan-structural (e.g. a global identifier like `Math` not in the activation slot map).
- `CallDependency` from `HasCallDependency` (`_hasNonParameterCalleeCall && !canUseDynamic`) is also
  gated out by the fast-path check. Only plan-structural call declines reach `Evaluate`.

### Change 3: `CallInvocationBoundary` code for out-of-boundary call shapes

The plan-level "out-of-boundary call shape" decline in `TryFindExpressionDecline` was using
`CallDependency` — the same code as the descriptor-level `HasCallDependency` decline. This ambiguity
meant `IsPlanStructuralDecline(CallDependency)` incorrectly excluded a structural decline.

Changed to use `CallInvocationBoundary` (an existing but unused enum member) for the plan-structural
decline when the call is not a direct eval. Direct eval keeps `CallDependency` since it has
context-dependent semantics.

## Final Signal

Full test suite: `4708 tests passed`.

Repeated selected-profile timing after the change:

```text
profile                 asynkron_ms  jint_ms  delta
simplearithmetic                234       77  Jint 3.04x faster
simplearithmetic                256       81  Jint 3.16x faster
simplearithmetic                265       82  Jint 3.23x faster
```

Final timestamp: 2026-05-30T21:40:00Z
Final signal: `simplearithmetic` Asynkron (3-run average) = 252ms (runs: 234, 256, 265)
Signal delta: −34ms (286ms → 252ms), **−11.9% improvement**, Jint ratio 3.64x → 3.14x

The improvement cleared the required ≥10% bar averaged over 3 runs.
