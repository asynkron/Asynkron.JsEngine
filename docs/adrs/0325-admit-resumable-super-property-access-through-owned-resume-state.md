# ADR 0325: Admit resumable super property access through owned resume state

## Status

Accepted

## Context

Issues
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-5219b4f05e`
/ PR #3187 and
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-a28e4dff02`
/ PR #3188 widened the resumable unified-bytecode route for class methods that
read, write, or update `super` properties across `yield` / `await`
suspension points.

The ordinary sync unified-bytecode route already owned the super-property
opcode families, but resumable execution had a separate safety boundary:
`TryFindUnsupportedResumableOpcode` omitted the super-property opcodes, and
`ExecuteResumable` had no handlers for them. At the same time, resumable class
methods cannot reconstruct their home-object/super binding per step from a
normal sync invocation frame. PR #3187 exposed the async-generator variant of
that boundary: slot initialization can use the synthetic resumable environment,
but `super` lookup needs the method environment normally materialized by
`ExecutionPlanRunner.GetOrCreateExecutionEnvironmentForInternalUse()`. The
route is only correct if the class-method activation metadata is captured on
`UnifiedBytecodeResumeState` and reused for every resumed VM step.

The final proof pass also exposed a test-log wrinkle: class method invocations
may log as `func=<anonymous>` because the method AST does not always carry the
JavaScript property name as a function name. Route assertions for class-method
resumable tests must therefore prove the route marker and activation shape
without depending on a user-visible method name.

## Decision

Admit resumable `super` property reads, writes, and updates only when the
resumable VM owns the full activation and opcode contract.

- Capture the method home-object/super activation metadata on
  `UnifiedBytecodeResumeState`, alongside the existing long-lived resumable
  state such as slots, operand stack, and program counter.
- For async generators, create the runner-owned method environment only when
  the accepted program requires resumable super lookup, and pass that
  environment as `UnifiedBytecodeResumeState.CallingEnvironment` while keeping
  ordinary slot storage on the resumable route.
- Add the super-property opcode families to the resumable opcode allowlist only
  together with matching `ExecuteResumable` handlers:
  `EnsureSuperReference`, `GetNamedSuperProperty`,
  `GetComputedSuperProperty`, `SetNamedSuperProperty`,
  `SetComputedSuperProperty`, `UpdateNamedSuperProperty`, and
  `UpdateComputedSuperProperty`.
- Preserve the existing no-mixed-execution rule: unsupported super calls,
  super construct shapes, private/exotic neighbors, and delete-super shapes
  must decline before VM entry instead of falling back inside the resumable VM.
- Pair future resumable super-property widening with positive route proof,
  opcode-stream proof, and adjacent retained-decline proof.
- In class-method route-log assertions, allow the established anonymous method
  logging shape when proving the resumable fast path.

## Consequences

- Resumable super-property semantics stay a VM-owned state problem instead of a
  per-step activation reconstruction shortcut.
- Future resumable opcode widening must continue to update eligibility,
  allowlists, VM handlers, expansion-contract inventories, and proof packs in
  one slice.
- The boundary remains narrow: ordinary super property access is admitted, but
  unowned super invocation/construct/delete forms continue on their existing
  routes.

## Evidence

- Delivery PR #3187 merged as commit `892e1e602`.
- Delivery PR #3188 merged as commit `054be1c9f`.
- PR #3187 added the read-focused async-generator repair in
  `TypedAstEvaluator.AsyncGeneratorInvoker`, using the runner-owned method
  environment as `UnifiedBytecodeResumeState.CallingEnvironment` only when
  `RequiresResumableSuperEnvironment(program)` is true.
- PR #3187 added
  `UnifiedBytecodeResumableSuperPropertyReadTests`, covering generator, async,
  and async-generator named/computed `super` reads after suspension with
  resumable fast-path route assertions.
- The final focused proof commit `49bfd4169` added B14 coverage for resumable
  super property write/update parity.
- Build-stage verification recorded:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeResumablePropertyWriteTests"`
    passed 19 tests.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeResumablePropertyUpdateDeleteTests"`
    passed 19 tests.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeResumableEligibility"`
    passed 3 tests.
  - `rtk git diff --check` passed.
- ADR allocation note: `rtk faktorial-api adr-next` was unavailable in this
  runtime (`No such file or directory`), so this learn pass used the Faktorial
  HTTP allocator endpoint. The first allocation returned `adr_id: 324`, which
  was already present on `main`; the next allocation returned the clean prefix
  `0325`.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/unified-bytecode-expansion-contract.md`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.UnifiedBytecodeResumableActivation.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumablePropertyWriteTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumablePropertyUpdateDeleteTests.cs`
