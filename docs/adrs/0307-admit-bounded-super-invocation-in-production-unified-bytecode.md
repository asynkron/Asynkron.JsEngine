# ADR 0307: Admit bounded super invocation in production unified bytecode

## Status

Accepted

## Context

ADR 0286 admitted synchronous construct calls to production unified bytecode and
kept the entire super invocation family declined because the route was
activation-gated and therefore unprovable. That was correct at the time:
derived constructors and super-using methods did not have an invoker-owned
activation setup that the flat-slot VM could consume safely.

PR #2862 widens that boundary in a bounded way. The delivery proves three
production-owned shapes:

- non-spread derived-constructor `super(...)` through
  `SuperConstructInvocationBoundary`;
- named super-member calls through `PrepareNamedSuperCallTarget` plus
  `CallInvocationBoundary`; and
- computed super-member calls through `PrepareComputedSuperCallTarget` plus
  `CallInvocationBoundary`.

The key change is not only new opcodes. The route becomes provable because the
invoker, selector, compiler, and VM now share one owned contract:

- `SyncFunctionInvoker` can create the required call environment and super
  binding for the admitted slice;
- production eligibility can admit the exact super invocation patterns while
  keeping adjacent super families declined;
- the compiler lowers those exact patterns to first-class unified-bytecode
  opcodes; and
- the VM executes them without calling back into `ExpressionProgram`,
  `ExecutionPlanRunner`, or AST evaluation.

This slice also preserves the earlier refusal to over-widen. Spread super
constructs, super property reads/writes/updates, instance-field-dependent
derived constructors, and other activation-heavy neighbors remain pre-VM
declines.

## Decision

Admit only the first bounded production super invocation shapes:

1. **Derived-constructor `super(...)`**
   - Accept only the non-spread derived-constructor shape.
   - Keep activation ownership in `SyncFunctionInvoker`: the admitted path still
     requires a real `new.target`, a super binding, derived-constructor `this`
     initialization, and existing post-call class-constructor completion rules.
   - The VM owns `SuperConstructInvocationBoundary`: resolve the dynamic super
     constructor from the active super binding, invoke `[[Construct]]` with the
     caller's `new.target`, initialize `this`, and preserve the existing
     double-super-call and invalid-return behavior.

2. **Super-member calls**
   - Accept only named and computed super-member call targets.
   - Lower them to `PrepareNamedSuperCallTarget` /
     `PrepareComputedSuperCallTarget` followed by the existing
     `CallInvocationBoundary`.
   - VM execution must resolve the property through the current super binding
     and invoke it with the derived receiver as `this`.

3. **Retained declines**
   - Spread super constructs stay declined.
   - Super property reads, writes, updates, and other super-adjacent families
     stay declined until a later slice owns their full selector/compiler/VM
     semantics and proof pack.
   - Derived-constructor routes that require instance fields or other unproven
     activation state stay declined before VM entry.

This ADR supersedes only the "keep super calls declined" portion of ADR 0286.
ADR 0286 remains the construct-call decision record.

## Consequences

- Production unified bytecode now owns the first demonstrable super invocation
  shapes instead of treating the whole family as categorically unreachable.
- Activation-gate analysis remains a real boundary, but no longer a blanket
  veto: once the invoker can supply the exact super environment contract,
  selector/compiler/VM widening becomes provable.
- Super invocation is still narrower than generic class-constructor or
  super-property support. Future widening must keep the owned-contract rule:
  invoker setup, eligibility admission, compiler lowering, VM semantics, and
  focused route proof all move in the same slice.
- ADR 0286's historical lesson still holds in one refined form: if a function
  kind is activation-gated, implementing VM semantics for its opcodes is dead
  code until the activation contract is widened and proven in the same change.

## Evidence

- Delivery PR #2862 merged as commit `6191df92`.
- Owned production surfaces widened together:
  - `TypedAstEvaluator.SyncFunctionInvoker`
  - `UnifiedBytecodeProductionEligibility`
  - `UnifiedBytecodeCompiler`
  - `UnifiedBytecodeProgram`
  - `UnifiedBytecodeVirtualMachine`
- Focused production proofs were part of the merged delivery:
  - `UnifiedBytecodeProductionEligibilityTests.Evaluate_SuperConstructExpressionPlan_AcceptsSuperConstructInvocationBoundary`
  - `UnifiedBytecodeProductionEligibilityTests.Evaluate_SuperCall_AcceptsSuperConstructInvocationBoundary`
  - `UnifiedBytecodeProductionEligibilityTests.Evaluate_SuperMemberCallExpressionPlan_AcceptsExecutableInvocationBoundary`
  - `UnifiedBytecodeProductionInvocationTests` super-member production-route assertions
  - `UnifiedBytecodeProductionConstructCallTests` derived-constructor `super(...)` runtime semantics
- Review-stage verification passed for the merged delivery:
  - focused production eligibility / invocation / construct tests: 567 tests;
  - broader super / activation regressions:
    `ClassSuperSemanticsTests|ActivationSemanticsProofPackTests|AstFreeExecutionAssertionTests`
    with 175 tests.

## Related

- `docs/adrs/0286-accept-unified-bytecode-construct-calls-and-decline-super-calls.md`
- `docs/adrs/0230-keep-derived-class-constructor-ir-activation-super-owned.md`
- `docs/adrs/0262-keep-unified-bytecode-named-member-call-receiver-owned.md`
- `docs/adrs/0263-keep-unified-bytecode-computed-member-call-key-and-receiver-owned.md`
- `docs/unified-bytecode-expansion-contract.md`
- `docs/rules/unified-bytecode-prototypes.md`
