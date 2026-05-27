# Unified Bytecode Branch Production Routing Evidence

Date: 2026-05-27
Issue: #2227

## Focused test proof

Command:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_DirectBranchReturnPlan_Accepts|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_NestedDirectBranchReturnPlan_DeclinesAsPrototypeOnlyJumpIfFalse|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.DirectBranchReturnFunction_UsesUnifiedBytecodeProductionFastPathForTrueAndFalseOutcomes|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.BinaryReturnFunction_KeepsExistingSpecializedFastPath"
```

Result:

- `ok dotnet test: 4 tests passed, 0 warnings in 1 projects (1.5 s)`

This proves:

- Direct `JumpIfFalse` branch-return eligibility is accepted.
- Nested adjacent `JumpIfFalse` shape remains declined.
- Invocation routes accepted branch-return through `unified-bytecode-production-fast-path`.
- Existing binary specialized fast path remains prioritized.

## Focused profile signal

Command:

```bash
rtk ./tools/profile forloop --memory
```

Result excerpt:

- `MEMORY PROFILE: ProfileRunner`
- `Total allocated 6.80 MB`

No allocation regression signal was observed during this routing-slice update.
