# ADR 0353: Keep private-name class constructors off production bytecode until lexical state is owned

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-02-cla-ec8d71f1da`
/ PR #3321 handled the A7 remaining boundary for production unified-bytecode
class constructor activation. Earlier class-constructor widening had already
proved public base constructors, public derived `super(...)` constructors, and
public instance-field initializer constructors through the production VM.

The remaining private-name constructor shapes looked deceptively close to that
admitted public route. Some constructors include direct private operations in
the body, such as `this.#p = v`; others only belong to a class with private
brand state, while the constructor body itself contains no private expression
operation. A body-only opcode check can miss the second shape and route it
through a constructor bridge that has not proven private-name lexical state.

Private brands and private member lookup are not receiver-only concerns. The
constructor activation path must also preserve the class private-name scope and
any captured private-name scopes that member initializers or methods depend on.

## Decision

Keep production unified-bytecode class constructor activation declined whenever
the constructor callable carries private-name lexical state:

- base class constructors require `PrivateNameScope is null` and no captured
  private-name scopes before routing;
- derived class constructors require the same private-name checks before
  routing, even when the `super(...)` body shape is otherwise admitted;
- private-name classes whose constructor bodies have no private expression op
  still decline, because the class brand/private-name environment is state the
  current constructor VM bridge does not own;
- public constructor routes remain admitted when they satisfy the existing
  parameter, `super(...)`, field-initializer, and plan-shape gates.

## Consequences

- Future A7 widening must prove private-name lexical-state setup at the
  constructor activation boundary, not merely add private field/member opcodes
  to the VM.
- Route tests must include a private-brand-only constructor body so the selector
  cannot regress into a body-op-only private-name check.
- Existing private-brand correctness still runs through the established
  class-construction path until the production constructor bridge owns that
  lexical state directly.

## Evidence

- PR #3321 merged as squash commit
  `ba2eb8921c2005bce98dcc0c7b9a01354f66e0ab`.
- Delivery commit before squash:
  `c6c9ab6b2a61df503740f90d1ee2795017b436b9`.
- Implementation changed
  `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs` by
  adding private-name scope checks to both base and derived class constructor
  production-route predicates.
- Focused proof lives in
  `tests/Asynkron.JsEngine.Tests/ClassConstructorActivationAdmissionTests.cs`
  for private-field-access, private-brand-only base constructor, and
  private-brand-only derived constructor declines.
- Public construct-call route tests in
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionConstructCallTests.cs`
  were aligned to assert no production route for private field/method class
  constructors while still proving brands initialize correctly on the existing
  path.
- Build-stage verification recorded
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionConstructCallTests"`
  passing 35 tests,
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ClassConstructorActivationAdmissionTests"`
  passing 13 tests, and `rtk git diff --check` passing.
- ADR allocation note: the local `rtk faktorial-api adr-next` wrapper was not
  available in this worker, so this learn pass used the runtime allocator
  endpoint `POST /api/adrs/next`, which returned `{"adr_id":353}`.

## Related

- `docs/adrs/0225-keep-base-class-constructor-ir-activation-binder-guarded.md`
- `docs/adrs/0327-admit-resumable-class-literal-private-members-through-shared-class-creation.md`
- `docs/rules/ecmascript-private-names.md`
- `docs/rules/unified-bytecode-prototypes.md`
