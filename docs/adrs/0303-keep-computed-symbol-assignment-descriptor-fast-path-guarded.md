# ADR 0303: Keep computed symbol assignment descriptor fast path guarded

## Status

Accepted

## Context

Issue gh2843 / PR #2846 targeted the allocation owner isolated by the
`symbol-propertyaccess` workload. The prior gh2829 evidence showed that the
computed symbol assignment path was still materializing `PropertyDescriptor`
objects through `JsObject.GetOwnPropertyDescriptor` for ordinary
`obj[symbol] = value` writes, even when the property was an existing writable
data property whose descriptor attributes did not need to change.

The retained delivery added a fast path in
`AssignmentReferenceResolver.AssignObjectProperty(...)` before descriptor
materialization:

- use the original assignment receiver value;
- continue only when the receiver unwraps to the exact target `JsObject`;
- update only through `TrySetExistingJsValue(...)`; and
- fall back to the existing descriptor/prototype/accessor path for every other
  case.

Focused proof used:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --filter "FullyQualifiedName~SymbolTests"
rtk ./benchmark.sh --allocations symbol-propertyaccess
```

The focused symbol tests passed 19/19. The selected benchmark moved from
`11184 ms` / `258936.7 KB` at 2026-05-31T12:07:19Z to `7220 ms` /
`8960.8 KB` at 2026-05-31T14:18:00Z.

## Decision

Keep descriptor-allocation cuts in assignment property writes guarded by receiver
identity and existing-data-property semantics.

For `AssignmentReferenceResolver.AssignObjectProperty(...)` and adjacent
ordinary property assignment fast paths:

1. bypass `GetOwnPropertyDescriptor(...)` only when the receiver is the same
   object as the assignment target;
2. use a storage helper such as `TrySetExistingJsValue(...)` that succeeds only
   for existing writable data properties and preserves descriptor flags;
3. preserve the descriptor path for accessors, non-writable properties, inherited
   setters, proxies, typed-array exotic behavior, private slots, and non-target
   receivers; and
4. prove the slice with focused semantic tests for writable descriptor flag
   preservation and strict-mode non-writable failure, plus the selected
   allocation benchmark when making a performance claim.

## Consequences

- Common computed symbol writes avoid descriptor materialization without
  weakening the observable ECMAScript assignment path.
- The fast path remains an owner-local runtime optimization rather than a
  broad rewrite of symbol storage or prototype assignment semantics.
- Future descriptor-allocation work should first prove that a candidate write is
  an existing writable own data-property update. If that proof is unavailable,
  keep the descriptor path.
- This ADR is caused by issue gh2843 / PR #2846.

## Related

- `docs/performance/symbol-propertyaccess-owner-evidence.md`
- `docs/rules/js-spec-property-access.md`
- `docs/rules/performance-profiling-guardrails.md`
