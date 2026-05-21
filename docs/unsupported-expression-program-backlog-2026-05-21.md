# UnsupportedExpressionProgram Backlog Baseline (2026-05-21)

## Scope
This baseline reports the current `UnsupportedExpressionProgram` classification buckets from the active compiler/diagnostic surfaces:

- `ExpressionProgramCompiler.ClassifyFailure(...)`
- `ExecutionPlanBuilder.SetExpressionProgramFailure(...)`
- `ExecutionPlanDiagnostics.DetailedSnapshot().ExpressionFailureCodes`

This document is backlog evidence only. It does not claim bytecode support changes or performance wins.

## Current buckets
Current bucket set (`ExpressionProgramFailureCode`):

- `UnsupportedExpressionNode`
- `UnsupportedDeleteTarget`
- `UnsupportedUnaryOperator`
- `UnsupportedUpdateTarget`
- `SuperCall`
- `NestedOptionalCall`
- `UnsupportedCompoundAssignmentShape`
- `OptionalOrSuperMemberUpdate`
- `SuperTaggedTemplate`
- `OptionalTaggedTemplate`
- `NestedOptionalTaggedTemplate`
- `OptionalOrSuperPropertyAssignment`
- `OptionalOrSuperIndexAssignment`
- `SuperMemberAccess`
- `UnsupportedObjectMemberKind`
- `UnsupportedStaticObjectPropertyName`
- `InvalidComputedObjectKey`
- `UnsupportedDotAccessPropertyName`
- `UnsupportedDirectMemberCallPropertyName`
- `UnsupportedTaggedTemplateMemberAccessName`
- `OptionalOrSuperMemberCallTarget`

## Ranked implementation backlog

### First slice (lower risk / normalization-friendly)
- `UnsupportedStaticObjectPropertyName`: constrained object literal/property-name handling; mostly syntax-shape normalization.
- `InvalidComputedObjectKey`: narrow object key validation/classification seam.
- `UnsupportedDotAccessPropertyName`: literal property-name classifier gap.
- `UnsupportedDirectMemberCallPropertyName`: direct member-call property-name classifier gap.
- `UnsupportedTaggedTemplateMemberAccessName`: tagged-template member-access name classifier gap.
- `UnsupportedObjectMemberKind`: add isolated object member kind support path after explicit shape gating.
- `UnsupportedUnaryOperator`: add explicit unary-op coverage one operator at a time.

### Medium risk (requires careful lowering/ordering proof)
- `UnsupportedUpdateTarget`: update target shape coverage can affect observable ordering/reference semantics.
- `UnsupportedCompoundAssignmentShape`: compound-assignment lowering has sequencing and temporary-value risks.
- `UnsupportedDeleteTarget`: delete target semantics around references/member forms need focused proof.
- `NestedOptionalCall`: optional call lowering and short-circuit stack behavior need dedicated tests.

### High risk / defer
- `SuperCall`
- `SuperMemberAccess`
- `OptionalOrSuperMemberUpdate`
- `OptionalOrSuperPropertyAssignment`
- `OptionalOrSuperIndexAssignment`
- `OptionalOrSuperMemberCallTarget`
- `SuperTaggedTemplate`
- `OptionalTaggedTemplate`
- `NestedOptionalTaggedTemplate`

Rationale: `super` and optional-chain/tagged-template combinations couple to lexical home-object and short-circuit semantics, so they should remain in narrow, isolated slices with dedicated semantic proof.

## Reproducibility
Focused probe commands used for this baseline:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --filter "FullyQualifiedName~ExecutionPlanDiagnosticsTests.ExpressionProgramFailureClassification_CoversCurrentBacklogBuckets"
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --filter "FullyQualifiedName~ExecutionPlanDiagnosticsTests.DetailedSnapshot_UnsupportedExpressionProgramBuckets_MatchRepresentativeProbe"
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
```

The first test locks the current classification bucket set; the second keeps a representative diagnostics probe grounded in real build failures.
