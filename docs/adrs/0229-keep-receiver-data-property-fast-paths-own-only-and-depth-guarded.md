# ADR 0229: Keep receiver data-property fast paths own-only and depth-guarded

## Status

Accepted

## Context

Issue `autrun-ditg7nt935mg-6d4d6539dd` / PR #2367 optimized the recurring
optimizer `propertyaccess` profile by adding a receiver-aware ordinary-object
data-property read shortcut in `JsObject`.

The profiled hot path was:

```text
ExecuteInstructionLoop
-> HandleCompoundAssignmentSlot
-> HandleCompoundAssignmentSlotSlow
-> EvaluateExpressionProgram
-> GetProgramNamedPropertyValue
-> JsOps.TryGetPropertyValue
-> JsObject.TryGetProperty
-> JsObject.TryGetPropertyJsValue
-> JsObject.TryGetOwnPropertyJsValue
```

The retained helper, `TryGetSimplePropertyWithReceiver`, lets the receiver
overloads return stored own data properties directly when the object has no
virtual provider, no own descriptor for the requested name, and the name is not
a private slot. That keeps the hot `propertyaccess` workload at the ordinary
`JsObject` storage boundary while inherited accessors still receive the
original `JsValue` receiver through the semantic fallback.

Review caught an important edge in the first implementation: on a miss, the
helper recursed through the public receiver overload. Each ordinary
`JsObject` prototype restarted lookup from that public overload, which reset
`JsEngineConstants.MaxPrototypeChainDepth` instead of carrying the current
depth through the chain.

The repair commit `543b9389` made the helper own-only. Misses now fall back to
the existing depth-limited `TryGetPropertyJsValue(...)` traversal.

## Decision

Keep receiver-aware data-property read fast paths own-only unless the fast path
explicitly carries and enforces the existing prototype-depth state.

For `JsObject`, `JsOps`, and adjacent named-property read optimizations:

1. A receiver fast-path helper may return only a proven own simple data-property
   hit.
2. On a miss, use the existing semantic lookup path that carries
   `JsEngineConstants.MaxPrototypeChainDepth`; do not recurse through a public
   receiver overload that restarts depth at zero.
3. Keep prototype traversal, accessors, virtual property providers, private
   slots, non-`JsObject` property accessors, primitive prototype reads, and
   JavaScript throw propagation on semantic fallbacks.
4. If a future shortcut really needs to traverse prototypes itself, it must
   accept and increment the current lookup depth and prove the same boundary as
   `TryGetPropertyJsValue(...)`.
5. Pair timing evidence with focused semantic tests for own data shadowing,
   prototype getter receiver binding, prototype-depth cutoff, and single getter
   evaluation when the read sits inside a compound assignment.

## Consequences

- The `propertyaccess` profile can keep the ordinary stored-data win without
  weakening the runtime's global prototype-chain guard.
- Receiver-preserving read helpers stay compatible with ADR 0148 and ADR 0188:
  the original JavaScript receiver is preserved, but only the storage owner can
  decide when a direct stored-value return is safe.
- Future named-read work should treat prototype recursion in helper code as a
  semantic boundary, not as an implementation detail. Restarting the public
  overload in a loop can bypass depth guards even when every single call looks
  locally safe.
- The performance note
  `docs/performance/propertyaccess-receiver-data-read-fast-path.md` remains the
  measurement and repair transcript; this ADR owns the durable helper boundary.

## Evidence

- Merged delivery commit `7b54bfd0` landed PR #2367 with the receiver data-read
  fast path and review repair.
- Repair commit `543b9389` removed prototype recursion from
  `TryGetSimplePropertyWithReceiver`.
- Focused regression coverage includes
  `Receiver_Data_Property_Read_Still_Stops_At_Max_Prototype_Depth` in
  `tests/Asynkron.JsEngine.Tests/PropertyAccessFastPathTests.cs`.
- Build-stage verification passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~PropertyAccessFastPathTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests" -- xUnit.MaxParallelThreads=1 -timeout 20000`
  with 42 tests.
- The selected profile improved from the 2026-05-27 baseline
  `propertyaccess` Asynkron row of `2092 ms` to a final median `1493 ms`.

## Related

- `docs/adrs/0148-keep-context-property-reads-jsvalue-receiver-native.md`
- `docs/adrs/0188-keep-named-property-read-fast-paths-storage-owned-and-receiver-preserving.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `.claude/rules/js-spec-property-access.md`
- `docs/performance/propertyaccess-receiver-data-read-fast-path.md`
