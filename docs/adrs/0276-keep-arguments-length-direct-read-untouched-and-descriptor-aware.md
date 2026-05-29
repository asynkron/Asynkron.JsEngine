# ADR 0276: Keep arguments length direct read untouched and descriptor-aware

## Status

Accepted

## Context

Issue `autrun-diupj7a8kdqg-135f5b6bd4` / PR #2612 continued the
`activation-arguments-lite` optimizer chain after the lazy index descriptor
work in ADR 0265. The selected strict workload still repeatedly reads
`arguments.length` and direct numeric `arguments[i]` values, so the concrete
`JsArgumentsObject` remains observable.

Before this slice, initial `arguments.length` reads still paid the generic
property-name conversion and backing object lookup path even when the length
property was untouched. Numeric index reads also still had a remaining direct
path opportunity when no observable index descriptors had been materialized.

The retained delivery kept the optimization inside the arguments-object owner:
`JsArgumentsObject` can return the initial stored length directly while the
length property is untouched, and `JsOps.TryGetPropertyValueJsValue` can consume
that value before the generic object lookup path. Any `length` assignment,
delete, or `defineProperty` disables the direct length value and lets ordinary
object/prototype semantics decide the read.

## Decision

Keep `arguments.length` direct reads owned by `JsArgumentsObject` and valid only
while the initial length property is untouched.

The fast path may return the initial argument count directly only when
`JsArgumentsObject` still knows no observable operation has changed the
`length` property. Assignment, delete, and property definition must invalidate
that direct value before delegating to backing object behavior, so later reads
observe data-property changes, deletion, prototype fallback, and descriptor
semantics through the ordinary path.

Do not move this shortcut into a generic property-read helper as a special case
for property name `length`. The generic helper may detect the concrete
arguments object and ask it for an initial length value, but the arguments
object owns the mutation state and the fallback boundary.

Do not widen the retained numeric-index direct read into dense-array semantics.
Direct index reads remain descriptor-aware per ADR 0211 and lazy-descriptor
aware per ADR 0265.

## Consequences

- Strict observable-arguments workloads can avoid repeated generic lookup for
  untouched `arguments.length` without weakening mutation, deletion, descriptor,
  or prototype behavior.
- Future arguments-object length optimizations must prove both the untouched
  direct path and the invalidated fallback path.
- The focused activation proof pack remains the local confidence gate for this
  surface, with explicit coverage for `length` mutation and prototype fallback.
- Retained performance claims still need selected-profile before/after timing
  because benchmark samples for this short profile can be noisy.

## Evidence

- PR #2612 merged as commit
  `6d61343ff69b426e2a92ba8a353a950a5b0f3eea`.
- Build-stage delivery commit was `42843791` on
  `agent-go/task-autrun-diupj7a8kdqg-135f5b6bd4`.
- Performance note:
  `docs/performance/activation-arguments-direct-read-fast-path.md`.
- Focused `activation-arguments-lite` median improved from 683 ms to 603 ms,
  an 11.7% improvement.
- Focused Release proof passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ActivationSemanticsProofPackTests" -- xUnit.MaxParallelThreads=1 -timeout 20000`
  with 48 tests.
- `rtk git diff --cached --check` passed before the delivery commit.

## Related

- `docs/adrs/0211-keep-arguments-object-index-reads-storage-owned-and-descriptor-aware.md`
- `docs/adrs/0265-keep-arguments-lazy-index-descriptors-observable-and-integrity-synced.md`
- `.claude/rules/function-activation-proof-pack.md`
- `docs/performance/activation-arguments-direct-read-fast-path.md`
