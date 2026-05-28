# ADR 0255: Keep unified bytecode block lexical scopes program-slot owned

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-2f1dcc1cd5`
and PR #2490 widened production unified bytecode to cover simple
non-capturing block lexical scopes. The accepted lane added owned
`PushEnvironment` and `PopEnvironment` opcodes, admitted destructuring-free
`let` / `const` declarations compiled from `SimpleVariableDeclaration`, and
kept `BindingVariableDeclaration`, destructuring, `with`, direct eval,
captured or dynamic activation, per-iteration environments, and `using`
declarations outside the production boundary.

The delivery also exposed a slot-layout repair during conflict resolution.
The first implementation could accept a program whose non-root flat-slot
mappings existed while the root activation mapping was empty or partial. That
made invocation-time parameter population crash or write the wrong slot for
unrelated array and contextual-keyword tests. The final repair completed root
activation mappings, derived program slot count from all flat-slot ids, kept
parameter slot indices positional with `-1` for unused formals, and resolved
declaration / assignment targets through flat mappings instead of name fallback.

## Decision

Keep production unified-bytecode block lexical scopes program-slot owned.

- A production `UnifiedBytecodeProgram` owns one slot span whose `SlotCount`
  covers root activation slots and every admitted flat-mapped block scope slot.
  `SyncFunctionInvoker` rents that program-sized span, not only the root
  activation slot count.
- Root activation mappings must be complete even when `ExecutionPlan` already
  contains non-root flat-slot mappings. Missing root slots are mapped before
  slot names, lexical indices, parameter indices, or declaration targets are
  remapped.
- `ParameterSlotIndices` preserve formal-parameter positions. An unused or
  unmapped formal is represented with `-1`, and invocation skips that entry
  instead of compressing the array and shifting later arguments.
- `PushEnvironment` is an owned VM opcode that stamps the admitted scope's
  lexical flat slots as `JsValue.Uninitialized`. It does not create a
  `JsEnvironment`, call `ExecutionPlanRunner`, or do name lookup.
- `PopEnvironment` is an owned VM opcode for the admitted linear cleanup shape.
  It restores the bytecode control-flow boundary without creating a runtime
  environment stack in the VM.
- `SimpleVariableDeclaration` for `let` and `const` may store into the active
  scope only when the compiler can resolve the declaration symbol through the
  active scope's flat-slot mapping. `var` continues to resolve through the root
  activation mapping.
- The selector must keep `BindingVariableDeclaration`, object/array
  destructuring driver instructions, `with`, direct eval / dynamic lookup,
  captured activation, per-iteration bindings, and `using` / `await using`
  declined before VM execution until a later slice owns those semantics end to
  end.

## Consequences

- Shadowed block bindings can run through production unified bytecode without
  falling back to symbol-name lookup or leaking into the root activation slots.
- The sync invocation bridge is no longer activation-slot-only; future
  production widening must treat `UnifiedBytecodeProgram.SlotCount` and
  remapped program metadata as the runtime contract.
- A block-scope opcode addition is incomplete if it omits the invocation bridge
  or parameter-position repair. Focused production tests are not enough; the
  canonical internal quality gate can expose unrelated callers whose parameter
  slots exercise partial root mappings.
- Dynamic scope, closure capture, destructuring, and iterator/per-iteration
  environment families remain deliberate fallback boundaries, not TODOs to
  paper over with VM callbacks.

## Evidence

- PR #2490 merged commit
  `ef7051895fc2d7c71e1d9c21bb488f339789ead5`.
- Build-stage proof recorded
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProduction" -- xUnit.MaxParallelThreads=1 -timeout 20000`
  with 175 tests passing before conflict repair.
- Conflict-resolution proof recorded the final production unified-bytecode pack
  passing with 186 tests, the seven original quality-gate regressions passing,
  and final `rtk make quality` passing.
- Review-stage proof recorded `rtk dotnet build` passing with 11 projects,
  `rtk git diff --check dfa8e0a6fa9b4a64e697a0289a14212afd6071ab HEAD`
  clean, the AST-eval seam scan returning no matches, and
  `rtk ./tools/profile forloop --memory` completing with total allocated
  6.75 MB.

## Related

- `docs/unified-bytecode-expansion-contract.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0248: `docs/adrs/0248-keep-unified-bytecode-primitive-operators-vm-owned-and-tdz-aware.md`
- ADR 0251: `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- ADR 0253: `docs/adrs/0253-keep-unified-bytecode-loop-control-targets-compiler-owned.md`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
