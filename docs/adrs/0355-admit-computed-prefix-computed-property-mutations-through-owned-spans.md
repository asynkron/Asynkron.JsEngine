# ADR 0355: Admit computed-prefix computed property mutations through owned spans

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-03-com-7f9a4a6544`
and delivery PR #3329 narrowed the A51j unified-bytecode mutation bucket. The
remaining stale decline was a property mutation whose receiver starts with a
computed property-read prefix and whose terminal write/update is also computed,
for example `box[k1].child[k2] = value`.

The production route already owned both parts independently:

- simple computed property-read receiver prefixes such as `box[k1].child`
  through the shared computed-read span measurer;
- direct computed writes, compound writes, logical writes, updates, and deletes
  when the terminal key and value regions were already admitted.

The missing ownership was the composition boundary. Treating the whole shape as
an unstructured property write would keep a stale `PropertyWriteDependency`
decline. Admitting it by adding a one-off syntax exception would risk evaluating
the receiver or key more than once, especially for compound/logical assignments
that read before writing.

## Decision

Admit computed-prefix computed property mutations only when the receiver prefix
and terminal key/value regions can be measured as already-owned unified-bytecode
spans.

- The receiver prefix must validate through
  `TryMeasureSimpleComputedPropertyReadOperandSpan`, so the prefix is resolved
  once and leaves exactly one receiver value for the terminal mutation.
- The terminal computed key must validate through the existing supported
  computed-key span checks, and RHS regions must use either the simple operand
  path or the already-admitted single-result region walker.
- Compound and logical writes reuse the existing old-value read, branch, binary,
  store, and cleanup contracts after the receiver prefix has been consumed.
- `AllowNameInference` on terminal computed writes remains a hard decline until
  the VM owns the relevant `NamedEvaluation` semantics for that property-write
  form.
- Unproven multi-computed receiver-prefix shapes and call-bearing receiver/key
  spans remain declined by the existing production gates.

## Consequences

- A51j no longer treats `box[k1].child[k2] = value`, `+=`, `&&=`, `||=`, `??=`,
  and the adjacent update/delete families as outside the production property
  mutation boundary when their spans satisfy the same route-owned contracts.
- Future property-mutation widening should compose measured spans rather than
  add source-syntax exceptions. The proof target is stack and evaluation-order
  ownership, not the spelling of the member expression.
- The expansion contract's compiler decline-template list is part of the
  deliverable. If a new compiler reason is added or retained, the contract must
  be updated in the same slice so the ratchet does not fail later as a build-back
  repair.

## Evidence

- Delivery PR #3329 merged as commit
  `708ffa411869f307ec8fdc71e0d5a0d2a8e6cb860`.
- Delivery changed:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/ProductionRouteCoverageRatchetTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
  - `docs/bytecode-progress.md`
  - `docs/unified-bytecode-expansion-contract.md`
- Focused tests covered:
  - `Evaluate_ComputedPrefixComputedPropertyWrite_AcceptsOwnedPropertyOpcodes`
  - `Evaluate_ComputedPrefixComputedCompoundPropertyWrite_AcceptsOwnedPropertyOpcodes`
  - `Evaluate_ComputedPrefixComputedLogicalPropertyWrite_AcceptsOwnedPropertyOpcodes`
  - `ComputedPrefixComputedPropertyWrite_UsesUnifiedBytecodeProductionFastPathAndResolvesPrefixAndKeyOnce`
- Build-back repair commit `0d5b92edc` added the missing compiler decline reason
  template `Computed-prefix computed property writes with name inference are not
  supported.` to `docs/unified-bytecode-expansion-contract.md`.
- Build-stage verification recorded:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests.UnifiedBytecodeCompiler_DeclineReasonTemplatesMatchExpansionContract"`
    passing 1 test.
  - `rtk git diff --check` passing.
- ADR allocation note: the local `rtk faktorial-api adr-next` wrapper was not
  available in this runtime (`No such file or directory`), so the learn pass
  used the runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":355}`.

## Related

- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0238: `docs/adrs/0238-keep-unified-bytecode-compound-property-writes-get-for-set-owned.md`
- ADR 0246: `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- ADR 0293: `docs/adrs/0293-admit-logical-and-nullish-expressions-in-unified-bytecode-with-peek-jump-semantics.md`
- ADR 0354: `docs/adrs/0354-admit-private-named-call-targets-inside-complex-call-arguments.md`
- `docs/rules/expression-bytecode-assignment.md`
- `docs/unified-bytecode-expansion-contract.md`
