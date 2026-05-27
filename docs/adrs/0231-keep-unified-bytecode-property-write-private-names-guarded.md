# ADR 0231: Keep unified bytecode property-write private names guarded

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-define-5cc93efb5a`
and PR #2379 widened production unified-bytecode routing from property reads
into a first property-write/update boundary.

The accepted boundary is deliberately narrow: activation-resolved ordinary
property bases, simple values or computed keys, owned unified VM opcodes, and
pre-VM declines for adjacent families such as statement/discarded writes,
compound/logical assignments, `super`, optional chaining, calls, object
literal/spread payloads, dynamic activation, `this`, `new.target`, `arguments`,
destructuring, and private fields.

Review found a boundary hole before the delivery merged. Private member access
can surface in expression bytecode as named property operations carrying a
private-name string, so accepting ordinary `GetNamedProperty`,
`SetNamedProperty`, or `UpdateNamedProperty` by opcode alone can accidentally
route private-field semantics through ordinary property opcodes. That would
bypass private-name lexical resolution, brand checks, getter/setter behavior,
and private-field errors.

## Decision

Keep production unified-bytecode property read/write/update routing guarded
against private-name strings before both selector acceptance and compiler
emission.

- Scan expression-program named property read/write/update operations for
  private-name strings before accepting a production candidate.
- Decline those shapes with `PrivateFieldDependency`, not a generic property
  boundary miss.
- In compiler shape probes, reject private-name strings before appending owned
  `GetNamedProperty`, `SetNamedProperty`, or `UpdateNamedProperty` opcodes or
  writing string constants.
- Keep accepted ordinary property writes/updates fallback-free: if the selector
  accepts a shape, the compiler and VM must execute it through owned unified
  bytecode opcodes only.
- Do not treat non-computed private member access as ordinary named property
  access just because the lowered expression op is named.

## Consequences

- Private field reads, writes, and updates remain outside the first production
  property boundary unless a future slice explicitly owns private-name
  semantics in the unified VM.
- Ordinary named property reads/writes/updates can keep using string constants
  and owned unified opcodes because private-name strings are filtered first.
- Future property-boundary widening must keep the selector, compiler emission,
  VM semantics, public route proof, and negative private-name tests aligned in
  the same slice.
- The propertyaccess performance evidence remains a boundary/baseline surface;
  it should not be read as a private-field or broad property-routing win.

## Evidence

- Delivery PR #2379 merged as commit
  `876fb031 Agent: task planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-define-5cc93efb5a (#2379)`.
- Build-back commit `3d5bec83 Guard unified bytecode private named properties`
  added the private-name guards before the PR was merged.
- `UnifiedBytecodeProductionEligibility` now detects private named property
  read/write/update operations and returns `PrivateFieldDependency`.
- `UnifiedBytecodeCompiler` now rejects private-name strings before emitting
  owned named property read/set/update opcodes.
- Focused negative coverage proves `receiver.#field`, `receiver.#field =
  value`, and `receiver.#field++` decline with `PrivateFieldDependency`.
- Build-stage verification passed
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests" -- xUnit.MaxParallelThreads=1 -timeout 20000`
  with 62 tests, `rtk git diff --check`, and the AST seam scan.

## Related

- `docs/adrs/0010-keep-private-name-scope-capture-on-function-instantiation.md`
- `docs/adrs/0218-keep-unified-bytecode-property-read-production-boundary-selector-owned.md`
- `docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`
- `docs/adrs/0222-keep-unified-bytecode-two-hop-named-property-read-boundary-owned.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `.claude/rules/ecmascript-private-names.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
