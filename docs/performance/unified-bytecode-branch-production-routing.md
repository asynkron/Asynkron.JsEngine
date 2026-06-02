# Unified Bytecode Production Routing Evidence

Date: 2026-05-27
Updated: 2026-06-02
Issues: #2227 (initial direct-branch slice), #2243 (expanded control-flow boundary), Batch 5 plan proof run (`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-a10f14eb22`)

## Current boundary summary

Current production boundary is ADR 0210, not direct-branch-only ADR 0204 wording.
Accepted programs execute all-or-nothing in `UnifiedBytecodeVirtualMachine`
without mixed fallback into `ExpressionProgram`, `ExecutionPlanRunner`, or AST
evaluators.

Eligible opcode/control-flow families for this production boundary:

- Core ops: `LoadSlot`, `LoadLiteral`, `StoreSlot`, `Return`.
- `Binary` subset: `+`, `-`, `*`, `/`, `%`, `<`, `<=`, `>`, `>=`.
- Control flow: `Jump` and `JumpIfFalse` only when emitted in compiler-owned
  accepted shapes (direct branches, branch joins, joined-local updates, and
  canonical condition-first loop back-edges).

Unsupported shapes decline before VM execution, including async/generator,
captured or dynamic activation, `arguments`, `this`, `new.target`,
calls/constructs, dynamic lookup, labels, break/continue, noncanonical loops,
unsupported payloads/opcodes/operators.

## Historical focused proof (#2227 direct branch slice)

Command:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_DirectBranchReturnPlan_Accepts|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_NestedDirectBranchReturnPlan_DeclinesAsPrototypeOnlyJumpIfFalse|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.DirectBranchReturnFunction_UsesUnifiedBytecodeProductionFastPathForTrueAndFalseOutcomes|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.BinaryReturnFunction_UsesProductionUnifiedBytecodeBeforeSpecializedFastPath"
```

Result:

- `ok dotnet test: 4 tests passed, 0 warnings in 1 projects (1.5 s)`

This historical slice proved:

- Direct `JumpIfFalse` branch-return eligibility is accepted.
- Nested adjacent `JumpIfFalse` shape remains declined.
- Invocation routes accepted branch-return through `unified-bytecode-production-fast-path` for both `pick(true)` and `pick(false)`.
- The original 2026-05-27 slice kept the binary specialized fast path prioritized. That route priority is superseded as of 2026-06-02: production unified bytecode now runs before simple binary fallbacks for admitted ordinary sync plans.

## Batch 5 production proof pack (current evidence)

Commands:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --memory
```

Results:

- Eligibility proof: 31 tests passed.
- Invocation proof: 17 tests passed.
- AST-eval seam scan: `NO_MATCHES`.
- Memory profile signal: `Total allocated 6.70 MB` then `Total allocated 6.80 MB`.

Interpretation:

- Allocation-stability signal only (minor run-to-run variance).
- No performance-improvement claim is made from this evidence alone.

## Focused profile signal (historical #2227 excerpt)

Command:

```bash
rtk ./tools/profile forloop --memory
```

Result excerpt:

- `MEMORY PROFILE: ProfileRunner`
- `Total allocated 6.80 MB`

No allocation regression signal was observed during this routing-slice update.
