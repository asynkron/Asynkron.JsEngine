# ADR 0386: Define immutable compile-artifact cache boundary

## Status

Accepted

## Context

ADR 0385 rejects storing accepted production unified-bytecode programs,
script-level production eligibility results, or other descriptor-sensitive
route-admission answers on `ExecutionPlan`. The rejected `forofiteration`
plan-level accepted-program cache still exposed a real repeated-work signal:
fresh sibling `SyncFunctionInvoker` instances can repeatedly pay parse, lower,
eligibility, and compile costs for structurally identical source.

`docs/dreaming.md` component 15 remains directional and describes a future
Compilation Artifact Cache for repeated-script startup work. Before runtime
implementation starts, the boundary needs to separate a safe immutable
compile artifact from accepted runtime programs and descriptor-sensitive route
decisions.

The reviewed owner surfaces are:

- `ExecutionPlan`, which owns immutable lowered plan data and plan-pure cached
  facts such as permanent structural declines and dependency-scan booleans.
- `TypedAstEvaluator.SyncFunctionInvoker`, which owns descriptor-sensitive
  route-admission state, `new.target` keying, accepted
  `UnifiedBytecodeProgram` storage, and invalidation when function shape
  mutators change private names, super/home object state, constructor state, or
  instance fields.
- `UnifiedBytecodeProductionEligibility`, which turns a plan plus
  `UnifiedBytecodeProductionActivationDescriptor` into an accept/decline
  answer and may carry a compiled `UnifiedBytecodeProgram`.
- `UnifiedBytecodeCompiler`, which lowers an `ExecutionPlan` plus compile
  flags into an executable `UnifiedBytecodeProgram`.
- `UnifiedBytecodeProgram`, which is immutable executable VM input but is not,
  by itself, the smallest safe shared compile artifact because an accepted
  program is reached only after descriptor-sensitive route admission.

## Decision

Define the future cache target as an immutable, non-executable compile artifact
whose key is source and compile-context data only. The smallest safe shape is a
lowered/source artifact bundle, not an accepted runtime program. A concrete
implementation can choose the exact type later, but it must stay equivalent to
an `ExecutionPlanBuildResult`-style bundle: parsed/lowered plan data, slot
layout metadata, source map/debug metadata, and compile fingerprints needed to
rebuild or evaluate route eligibility.

The cache key must include at least:

- exact source bytes or text hash;
- realm-independent compile options that affect parsing or lowering;
- script/module mode and strictness inputs;
- host-supplied compilation options;
- engine/compiler version or lowered-artifact fingerprint;
- any feature flags that alter emitted `ExecutionPlan` or expression payload
  shape.

The cached artifact must not include:

- accepted or rejected production route-admission answers;
- `UnifiedBytecodeProductionEligibilityResult`;
- accepted `UnifiedBytecodeProgram` instances;
- `new.target`-dependent answers;
- activation-descriptor answers or function-shape decisions owned by
  `SyncFunctionInvoker`;
- invalidation state for descriptor, private-name, super/home-object,
  constructor, prototype, or instance-field mutators.

`ExecutionPlan` remains allowed to cache plan-pure facts and permanent
structural declines. `SyncFunctionInvoker` remains the owner for accepted
runtime programs, descriptor-sensitive route state, `new.target` keying, and
shape-mutator invalidation. Eligibility and compiler code remain the boundary
where a cached non-executable artifact can become an accepted executable
program for a specific invoker context.

Component 15 in `docs/dreaming.md` should keep startup and Node.js parity
claims directional unless fresh profile evidence is attached. Any later step
that wants to cache an executable `UnifiedBytecodeProgram` directly must record
a separate ADR and prove the descriptor and activation inputs that make it
safe.

## Consequences

- The first implementation target is narrower than the previous component 15
  wording: cache hits may skip parse/lower/build work, but they do not skip
  descriptor-sensitive route eligibility unless another ADR proves that owner
  boundary.
- `forofiteration` still has a valid repeated-work follow-up, but the proof
  target is an immutable lowered artifact rather than a plan-owned accepted
  production program.
- Future optimizer runs have a positive boundary to pair with ADR 0385's
  negative precedent.

## Evidence

- ADR allocation note: `rtk faktorial-api adr-next` was unavailable in this
  runtime (`rtk: No such file or directory`). The Faktorial runtime allocator
  endpoint `POST /api/adrs/next` returned `{"adr_id":386}`, and the `0386`
  prefix was checked free before writing.
- ADR 0385:
  `docs/adrs/0385-reject-plan-level-production-ubc-accepted-program-cache.md`.
- Component 15:
  `docs/dreaming.md` section "15. Compilation Artifact Cache (directional)".
- Failed-cache precedent:
  `docs/performance/failed-forofiteration-plan-level-production-bytecode-program-cache.md`.
- Source owner surfaces reviewed:
  - `src/Asynkron.JsEngine/Execution/ExecutionPlan.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`

## Related

- ADR 0378:
  `docs/adrs/0378-cache-production-ubc-dependency-scans-on-executionplan.md`
- ADR 0379:
  `docs/adrs/0379-cache-production-ubc-route-eligibility-on-sync-invoker.md`
- ADR 0385:
  `docs/adrs/0385-reject-plan-level-production-ubc-accepted-program-cache.md`
