# ADR 0283: Accept this-dependent async/generator functions in resumable unified bytecode production

## Status

Accepted

## Context

Faktorial issue #2675 widened the **resumable** unified bytecode production
route to accept async and generator functions that reference `this`. This is
the resumable-route counterpart to the ordinary sync `this` support landed in
#2633/#2643 (ADR 0279).

Before this slice the resumable route declined `this`-dependent activations in
two places:

- `UnifiedBytecodeProductionEligibility.EvaluateResumable` declined whenever the
  activation descriptor flagged `HasThisDependency`. The async invoker set
  `HasThisDependency: !thisValue.IsUndefined`, so every async method invoked
  with a receiver declined before VM execution.
- `UnifiedBytecodeProductionEligibility.TryFindUnsupportedResumableOpcode` did
  not list `LoadThis` among the resumable-supported opcodes, so any compiled
  generator/async program that emitted `LoadThis` declined as an unsupported
  resumable opcode. (The sync generator invoker never set `HasThisDependency`,
  so this opcode gate was the only thing keeping `this`-using generators safe.)

The resumable execution loop (`UnifiedBytecodeVirtualMachine.ExecuteResumable`)
had no `LoadThis` case and its `UnifiedBytecodeResumeState` carried no `this`
value, unlike the non-resumable `Execute`, which already accepts a `thisValue`
parameter and pushes it for `LoadThis` (VM `:80`, `:175-176`).

The key lifetime constraint for the resumable route is that `this` must survive
suspension and resume across `yield`/`await`. Storing it on the long-lived
resume state at construction (alongside slots, operand stack, and program
counter) is the correct lifetime, mirroring how the non-resumable path threads
`thisValue` for the duration of a single `Execute`.

## Decision

Admit `this`-dependent async and generator programs to the resumable unified
bytecode route, threading the strict/sloppy-coerced `this` through VM-owned
resume state.

- Add `ThisValue` to `UnifiedBytecodeResumeState`, captured at construction so
  it survives suspension/resume.
- Add a `case LoadThis:` to the `ExecuteResumable` loop that pushes
  `state.ThisValue` (mirrors the non-resumable `Execute` `LoadThis`).
- Add `UnifiedBytecodeOpCode.LoadThis` to the resumable-supported opcode set in
  `TryFindUnsupportedResumableOpcode`; `LoadNewTarget` stays unsupported.
- Remove the `HasThisDependency` decline from `EvaluateResumable`. The
  `new.target`, captured/dynamic activation, arguments-object, call, and
  dynamic-lookup declines remain intact.
- Compute `boundThis = isStrict ? thisValue : CoerceThisValueForNonStrict(...)`
  in both `AsyncFunctionInvoker` and `SyncGeneratorInvoker`, and pass it into
  `UnifiedBytecodeResumeState`. `CoerceThisValueForNonStrict` is promoted to a
  shared static helper so the coercion is byte-for-byte identical to the sync
  production route.
- `HasThisDependency` is left in `UnifiedBytecodeProductionActivationDescriptor`
  as an explicit future gate; it is no longer set by any resumable invoker.

This intentionally narrows ADR 0277, which previously listed `this` among the
resumable pre-VM declines. ADR 0277's bounded-state and `yield*`-decline
decisions otherwise stand unchanged.

## Consequences

- `this`-dependent async and generator programs whose bodies otherwise satisfy
  the resumable gates now execute through the resumable unified VM instead of
  falling back to the IR generator/async runner.
- Strict and sloppy `this` semantics match the sync route exactly: strict
  preserves a primitive `this`; sloppy boxes primitives and resolves nullish to
  `globalThis` via the shared `CoerceThisValueForNonStrict`. The coercion runs
  in the invoker before VM entry, so the resumable `LoadThis` opcode always
  loads the pre-coerced value.
- `this` survives suspension/resume because it lives on the resume state for the
  lifetime of the generator/async activation, not just one VM step.
- Property reads such as `this.x` and `typeof this` remain outside the resumable
  opcode set and decline independently of this `this`-binding widening; only
  bare `this` flowing through resumable-supported opcodes (LoadThis, Yield,
  Binary, Return) is admitted today.
- `new.target` (`LoadNewTarget`), arguments-object, captured/dynamic activation,
  call, and dynamic-lookup resumable shapes still decline before VM execution.

## Evidence

- Issue #2675; builds on #2633/#2643 (ADR 0279).
- Proof pack `UnifiedBytecodeResumableThisBindingTests` (9 tests passing) covers:
  strict generator/async returning bound `this` through the resumable fast path,
  `this` read after a `yield` suspension and after an `await` suspension,
  strict-vs-sloppy primitive `this` fidelity (`this === arg` true in strict,
  false in sloppy where the primitive is boxed), and negative-fallback gates
  where `new.target` and arguments-object async/generator shapes still decline
  the resumable route and run via IR.
- `UnifiedBytecodeProduction*` suite (256 tests) passes with no regressions.

## Related

- ADR 0277: `docs/adrs/0277-keep-resumable-unified-bytecode-state-bounded-and-yield-star-declined.md`
- ADR 0279: `docs/adrs/0279-accept-this-dependent-ordinary-sync-in-unified-bytecode.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncFunctionInvoker.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncGeneratorInvoker.cs`
- `docs/unified-bytecode-expansion-contract.md`
