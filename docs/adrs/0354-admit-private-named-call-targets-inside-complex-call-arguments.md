# ADR 0354: Admit private named call targets inside complex call arguments

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-03-com-a678f38320`
and delivery PR #3327 narrowed the A51g unified-bytecode call-boundary
diagnostic bucket. The specific stale decline was private named method call
target preparation when the private call appeared as an argument to another
call, for example `sink(receiver.#read(value))`.

Direct private named method calls were already admitted through
`PrepareNamedCallTarget` and `CallInvocationBoundary`. Ordinary nested call
arguments were also admitted through the complex-call-argument region walker.
The remaining blocker was a private-name guard in that region walker: it treated
private named member call targets as unsafe even though the class method route
already threads the private-name scope needed for brand lookup.

This is not the same as broad private-name constructor or super-private
admission. The accepted shape stays inside an already routed class method body,
uses an explicit receiver, and lowers through the same named-member call target
constant model as the direct private method call.

## Decision

Admit private named method call targets inside complex call arguments when the
surrounding call span is otherwise already eligible.

- `LoadNamedCallTarget` with a private name may pass the complex-call-argument
  eligibility stack-shape check when it is non-optional and has no nullish
  short-circuit target.
- The compiler lowers the private name through `PrepareNamedCallTarget` and a
  `NamedMember` call-target constant, preserving the existing receiver/callee
  stack contract.
- The admitted route must stay paired with public runtime proof that the
  private method receives the correct `this` and reaches the production
  unified-bytecode fast path.
- Optional private calls, private super call targets, private constructor
  activation state, direct-eval widening, spread calls, and unproven
  private-adjacent receiver/key families remain separate boundaries.

## Consequences

- A51g no longer carries this stale private named call-target preparation
  diagnostic for nested ordinary call arguments.
- Future call-boundary work should treat private names as lexical-state
  dependent, not categorically unsupported. If the enclosing route already owns
  private-name scopes and receiver binding, a private named member call can be a
  normal named-member call-target shape.
- Future private-name widening must still prove both the lowering stream and
  runtime route behavior. Shape-only eligibility is not enough for private
  calls because brand lookup and `this` preservation are observable.

## Evidence

- Delivery PR #3327 merged as commit
  `793ef3bc7657d8e07de1b7090e71cfbed2b7ee4c`.
- The delivery changed:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
  - `docs/unified-bytecode-expansion-contract.md`
  - `docs/plans/bytecode-burndown-checklist.md`
- Focused tests added:
  - `Evaluate_PrivateNamedMethodCallArgument_AcceptsNestedCallTargetPreparation`
  - `PrivateNamedMethodCallArgument_UsesUnifiedBytecodeProductionFastPath`
- Build-stage verification recorded:
  - `rtk git diff --check origin/main...HEAD`
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"` passing 603 tests with existing nullable warnings.
- ADR allocation note: the local `rtk faktorial-api adr-next` wrapper was not
  available in this runtime (`No such file or directory`), so the learn pass
  used the runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":354}`.

## Related

- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0250: `docs/adrs/0250-keep-unified-bytecode-call-target-prep-boundary-non-executable.md`
- ADR 0261: `docs/adrs/0261-keep-unified-bytecode-call-invocation-boundary-plan-sliced-and-deferred.md`
- ADR 0262: `docs/adrs/0262-keep-unified-bytecode-named-member-call-receiver-owned.md`
- ADR 0263: `docs/adrs/0263-keep-unified-bytecode-computed-member-call-key-and-receiver-owned.md`
- `docs/rules/expression-bytecode-call-targets.md`
- `docs/unified-bytecode-expansion-contract.md`
