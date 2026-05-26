# ADR 0204: Keep unified bytecode sync production routing slot-bridged

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-aa82d6b615`
and PR #2217 made the unified bytecode VM a real production sync-invocation
route for the first time. The prior production-routing slice, PR #2205, had
defined a decline-first selector that accepts only neutral `LoadSlot`,
`LoadLiteral`, `StoreSlot`, and `Return` programs. This slice had to connect
that selector to `SyncFunctionInvoker` without changing ordinary JavaScript
activation semantics or shadowing existing faster sync-call routes.

The sync invoker already has several specialized routes: direct simple-return
numeric/binary shortcuts, the sync IR trampoline, and the generic simple IR
activation runner. The unified bytecode route is only safe when it agrees with
the current `ExecutionPlan` activation slot layout and when the call has no
observable dependency on `arguments`, `this`, `new.target`, captured/dynamic
activation, private names, `super`, home objects, instance fields, async, or
generator behavior.

## Decision

Route the first production unified bytecode subset through `SyncFunctionInvoker`
as an invocation-local slot bridge, not as a replacement activation model.

- Keep existing direct simple-return numeric/binary routes and
  `SyncIrCallTrampoline` ahead of unified bytecode. The unified route is a
  fallback before the generic simple IR activation runner, not a shadow over
  proven specialized fast paths.
- Use the current `ExecutionPlan` plus
  `UnifiedBytecodeProductionActivationDescriptor` as the only production
  selector input. Cache the accepted/declined result and accepted
  `UnifiedBytecodeProgram` per plan, and do not compile or invoke the VM on
  every call.
- Require ordinary sync activation gates before the VM route: undefined
  `newTarget`, no class or arrow function behavior, no async/generator shape,
  simple identifier parameters, no parameter expressions, no arguments-object
  dependency, no captured/dynamic activation, no lexical `this`, no home object,
  no private-name/super/instance-field state, no function-name parameter
  collision, and the existing simple IR activation plan-shape guard.
- Build the VM input as invocation-owned slot storage from
  `ActivationSlotShape`: rent a `JsValue` array sized to `SlotCount`, fill the
  active span with `undefined`, populate arguments through
  `ParameterSlotIndices`, execute `UnifiedBytecodeVirtualMachine`, then return
  the array cleared.
- Do not create a `JsEnvironment`, call `ExecutionPlanRunner`, or add VM
  fallback to existing expression/AST evaluators for accepted unified bytecode
  programs. Unsupported or unsafe shapes must decline before VM execution.
- Prove the bridge with public invocation tests that show positive routing,
  missing-argument `undefined` initialization, preservation of existing
  specialized fast paths, unsupported-call fallback, and the activation proof
  pack staying green.

## Consequences

- Unified bytecode now has a production runtime foothold without broadening the
  production opcode set beyond the selector contract.
- Slot layout remains plan-owned and runtime-consumed. Future production
  widening must update the selector, sync bridge, and tests together instead of
  inferring safety from source shape or prototype compiler coverage.
- Existing hot sync-call shortcuts remain preferred, so production routing can
  grow without accidentally regressing routes that already have stronger
  evidence.
- The accepted VM path is fallback-free by construction. Malformed accepted
  programs are implementation bugs, not permission to silently drop back to the
  statement IR runner.

## Related

- Issue `planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-aa82d6b615`
- PR #2217
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0181: `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
