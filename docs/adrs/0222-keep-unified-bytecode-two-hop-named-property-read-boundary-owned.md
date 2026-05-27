# ADR 0222: Keep unified bytecode two-hop named property-read boundary owned

## Status

Accepted

## Context

ADR 0218 and ADR 0221 enabled the first production property-read boundary for:
- direct named reads (`box.value`)
- direct computed reads (`box[key]`) with an exact opcode sequence

The next requested widening is `box.child.value`, but without reopening mixed
AST/IR execution. This shape is still property-read-only and can stay fully
owned by existing unified bytecode opcodes.

## Decision

Accept exactly one additional named-read shape in production eligibility:
- activation-resolved base identifier
- first non-optional `GetNamedProperty`
- second non-optional `GetNamedProperty`

Compile this accepted shape to existing owned opcodes only:
- `LoadSlot(base)`
- `GetNamedProperty(name1)`
- `GetNamedProperty(name2)`

Keep adjacent families explicitly out of scope:
- optional chains
- computed-in-chain forms (`box[key].value`, `box.child[key]`)
- calls/construct, writes/updates/delete, `super`, `this`, dynamic lookup

Do not add `ExpressionProgram`, `ExecutionPlanRunner`, or AST callback fallback
for this boundary widening.

## Consequences

- `box.child.value` can route to `unified-bytecode-production-fast-path`.
- Compiler and selector remain aligned on a strict boundary.
- Neighboring property shapes still decline before unified VM execution.
- Future property-read widening remains incremental and proof-driven.

## Evidence

- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  accepts exact two-hop named chains and keeps neighbor declines explicit.
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
  emits two `GetNamedProperty` opcodes for accepted chains.
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
  proves acceptance for `box.child.value` and declines for computed-in-chain
  neighbors.
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodePrototypeTests.cs`
  proves opcode and string-constant shape for the two-hop chain.
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
  proves fast-path routing and getter observability for the accepted chain and
  no-route behavior for a computed-in-chain neighbor.

## Related

- `docs/adrs/0218-keep-unified-bytecode-property-read-production-boundary-selector-owned.md`
- `docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
