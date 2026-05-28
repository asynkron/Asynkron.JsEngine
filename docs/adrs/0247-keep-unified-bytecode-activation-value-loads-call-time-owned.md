# ADR 0247: Keep unified bytecode activation-value loads call-time owned

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-9db8c7189a`
and PR #2473 widened production unified bytecode for the first activation-value
loads: ordinary sync-function `this` reads and ordinary-call `new.target`
reads.

The existing production route was already an invocation-local slot bridge from
`SyncFunctionInvoker` into `UnifiedBytecodeVirtualMachine`. It deliberately
declined class constructors, arrows, async/generator functions, parameter
expressions, observable `arguments`, captured or dynamic activation, home or
`super` state, private-name state, instance fields, and construct calls with
non-undefined `newTarget`.

That boundary made the safe activation lane narrow: the VM can read
caller-supplied activation values for accepted ordinary sync calls, but it must
not synthesize a function environment or infer that broader activation
semantics are safe. The delivery added `LoadThis` and `LoadNewTarget` to the
unified opcode surface, compiled `ExpressionOpKind.LoadThis` and
`ExpressionOpKind.LoadNewTarget`, and threaded the call-time values into the VM.
It reused the existing non-strict receiver coercion helper before execution, so
sloppy `this` still observes the same nullish-to-global and primitive-boxing
rules as the simple IR activation path.

The first quality pass also found contract drift: the expansion contract
document still listed the old opcode inventory. The fix updated
`docs/unified-bytecode-expansion-contract.md` with `LoadThis` and
`LoadNewTarget`, and the focused guard then passed.

## Decision

Keep production unified-bytecode activation-value loads call-time owned and
decline-guarded.

- `LoadThis` and `LoadNewTarget` are owned unified VM opcodes, not callbacks
  into `ExpressionProgram`, `ExecutionPlanRunner`, AST evaluation, or a
  synthetic `JsEnvironment`.
- `SyncFunctionInvoker` must supply the VM with invocation-local activation
  values. For `this`, strict calls pass the caller value through and sloppy
  calls use the existing `CoerceThisValueForNonStrict(JsValue)` helper before
  VM execution.
- Production routing may admit these loads only inside the existing ordinary
  sync boundary: undefined `newTarget`, no class/arrow/async/generator shape,
  no observable `arguments`, no dynamic or captured activation, no home/super
  or private state, and no unsupported expression payloads.
- `arguments` remains an explicit decline. Supporting it would require the
  arguments-object owner and activation binding semantics, not a simple VM
  load.
- Activation-value loads can participate in already-owned first-boundary
  property reads, writes, updates, and compound property writes only when the
  selector, compiler, VM, and public route proofs all stay aligned.
- Any opcode or decline-surface change in this area must update
  `docs/unified-bytecode-expansion-contract.md` in the same slice so the drift
  guard reflects the current runtime surface.

## Consequences

- Ordinary sync functions such as `return this.value` and `return new.target`
  can route through production unified bytecode when the call shape is already
  eligible.
- The VM remains fallback-free while still observing the caller's receiver and
  ordinary-call `new.target` value.
- Future activation widening must treat `this`, `new.target`, and `arguments`
  separately. The first two now have a narrow call-time value lane; `arguments`
  still belongs to the arguments-object and activation-binding contract.
- The expansion contract is part of the executable surface for parallel
  unified-bytecode work. Missing new opcode names should fail the focused guard
  before the lane is considered complete.

## Evidence

- PR #2473 merged commit `48e3de752d467d4e335a0233e21e35c92e6acfac`.
- Focused build-stage proof passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"`
  with 140 tests passing.
- The build-back guard proof passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"`.
- The accepted tests prove `this.value` emits `LoadThis`, ordinary-call
  `new.target` emits `LoadNewTarget`, and both selected runtime cases log
  `unified-bytecode-production-fast-path`.

## Related

- `docs/unified-bytecode-expansion-contract.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0218: `docs/adrs/0218-keep-unified-bytecode-property-read-production-boundary-selector-owned.md`
- ADR 0221: `docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`
- ADR 0232: `docs/adrs/0232-keep-sync-function-this-binding-jsvalue-native.md`
- ADR 0246: `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
