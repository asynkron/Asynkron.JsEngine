# ADR 0248: Keep unified bytecode primitive operators VM-owned and TDZ-aware

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-b7fbd79613`
and PR #2474 widened production unified bytecode for a broad primitive
operator lane. The accepted slice added owned VM opcodes for `typeof`,
`typeof` identifier, unary plus/minus/logical-not/bitwise-not/void,
`ToString`, `Pop`, and strict equality/inequality.

The lane was deliberately wider than a single operator, but still narrow in
ownership: the compiler flattens already-lowered `ExpressionProgram` operations
into unified instructions, the production selector admits only activation-owned
identifier shapes, and the VM executes the opcodes without calling back into
`ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation.

Two build-back lessons made the durable boundary sharper:

- the expansion contract initially missed the new primitive opcode inventory,
  so `ExpressionProgramCoverageMapTests.UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums`
  failed until `docs/unified-bytecode-expansion-contract.md` listed the new
  opcodes; and
- production unified activation slots initially left root lexical slots as
  `undefined`, which made `typeof x` and direct `x` reads before a later
  `let x` declaration lose temporal-dead-zone behavior. The repair initialized
  lexical slots as `JsValue.Uninitialized` and made VM `LoadSlot` /
  `TypeOfIdentifier` throw the same `ReferenceError` as the normal runtime.

ADR 0205 kept production `Binary` eligibility operator-explicit until each
operator had route proof. PR #2474 is the proof slice that admits
`StrictEqual` and `StrictNotEqual`; the rest of the unproven binary family
remains decline-guarded.

## Decision

Keep production unified-bytecode primitive operators VM-owned, helper-backed,
and TDZ-aware.

1. Primitive unary, conversion, stack-discard, and strict equality opcodes are
   owned unified VM instructions. They must not execute by delegating to
   `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluators.
2. Coercive opcodes must reuse the same semantic helpers as the expression
   program runner, such as `JsOps.ToNumber`, `TypedAstEvaluator.NegateValue`,
   `TypedAstEvaluator.BitwiseNot`, `JsOps.ToJsString`, and
   `JsOps.StrictEquals`, and must check `context.ShouldStopEvaluation` after
   helper calls that can throw.
3. `TypeOfIdentifier` is eligible only when the identifier resolves to an
   activation slot. Unresolved identifiers, including undeclared `typeof`
   probes, remain dynamic lookup declines rather than VM guesses.
4. Production unified invocation must initialize activation lexical slots from
   `ActivationSlotShape.LexicalSlotIndices` as `JsValue.Uninitialized` before
   parameter population. VM slot reads and `TypeOfIdentifier` must preserve TDZ
   `ReferenceError` behavior, including slot names when available.
5. `EvaluateAndDiscard` may compile only supported expression programs and must
   append a unified `Pop` so side effects and abrupt completions occur before
   the value is discarded.
6. Any future primitive opcode widening must update selector eligibility,
   compiler translation, VM semantics, public fast-path proof, nearby decline
   proof, `docs/unified-bytecode-expansion-contract.md`, the AST-eval seam
   scan, and the memory/profile stability signal in the same delivery slice.

## Consequences

- Ordinary sync functions that use the admitted primitive lane can route through
  `unified-bytecode-production-fast-path` while preserving observable
  JavaScript coercion, strict equality, abrupt completion, and TDZ behavior.
- The production bridge remains environment-free for accepted programs, but it
  still carries the lexical TDZ facts needed by VM slot reads.
- `typeof missing` still declines as dynamic lookup. The admitted path is
  `typeof` over activation-resolved names, not a broad undeclared-identifier
  implementation.
- Strict equality now supersedes the ADR 0205 production decline for `===` and
  `!==` only. Other binary operators still need their own operator-explicit
  proof before production routing widens.
- The repeated contract-inventory miss confirms that the expansion contract is
  part of the runtime delivery surface, not learn-stage cleanup.

## Evidence

- PR #2474 merged commit
  `c0c2acd3dcf0fd76c9281c90e2c7d642a59e4640`.
- Build-stage proof passed
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProduction"`
  with 147 tests before the TDZ build-back, then the final focused production
  pack passed with 149 tests.
- The expression lowering proof passed
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramLoweringTests&FullyQualifiedName~ReturnInstruction_"`
  with 51 tests.
- The AST-eval seam scan
  `rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`
  found no matches.
- `rtk ./tools/profile forloop --memory` succeeded with total allocated
  `6.72 MB`.

## Related

- `docs/unified-bytecode-expansion-contract.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0205: `docs/adrs/0205-keep-unified-bytecode-binary-production-eligibility-operator-explicit.md`
- ADR 0246: `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
