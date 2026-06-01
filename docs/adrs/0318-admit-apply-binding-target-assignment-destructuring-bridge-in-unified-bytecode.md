# ADR 0318: Admit ApplyBindingTarget assignment destructuring bridge in unified bytecode

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-c537361518`
and PR #2941 burned down another `DestructuringDependency` bucket for production
unified bytecode.

Before this delivery, object and array declaration destructuring had VM-owned
driver lanes for narrow direct-slot shapes, but expression-level assignment
destructuring that lowered through `ExpressionOpKind.ApplyBindingTarget` still
declined. That left important existing destructuring semantics, including
computed keys, defaults, nested patterns, rest, TDZ checks, iterator closing,
and assignment target resolution, outside the production route even when the
surrounding ordinary sync function was otherwise eligible.

The risk was adding a broad fallback from the unified VM into expression or
statement interpretation. That would violate the no-mixed-execution contract
and make production eligibility less auditable. The useful distinction was that
`ApplyBindingTarget` already points at a lowered `BindingTargetProgram`, not an
arbitrary AST subtree.

## Decision

Admit expression-level assignment destructuring through a bounded
descriptor-backed binding-target bridge.

- `UnifiedBytecodeProgram` owns a `BindingTargetConstants` table for lowered
  `BindingTargetProgram` descriptors.
- `UnifiedBytecodeCompiler` emits `ApplyBindingTarget` only when the expression
  operation references a lifted descriptor.
- The VM owns dispatch and operand state: it duplicates the assignment RHS to
  preserve expression bytecode stack semantics, applies the binding target in
  `BindingMode.Assign`, and continues in the same unified program.
- Before and after the bridge, the VM syncs unified slots with the activation
  environment so the shared binding-target program sees and writes the same
  values as the existing expression-runner path.
- The bridge calls
  `ApplyLoweredAssignmentBindingTargetProgram(..., allowNameInference: false)`.
  Assignment destructuring writes through binding targets and must not infer a
  name for an existing anonymous function value merely because a target name is
  present.
- This is not a general VM fallback. Generic binding declarations, unsupported
  destructuring driver shapes, and descriptor-ineligible assignment targets
  still decline before VM execution with `DestructuringDependency` or the more
  specific owning decline.

## Consequences

- Production unified bytecode can now route ordinary sync functions containing
  assignment destructuring with computed/default/nested/rest semantics while
  preserving the existing binding-target semantics.
- The no-mixed-execution rule stays intact because the VM does not call
  `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation; it invokes a
  lowered binding-target descriptor with explicit slot/environment sync.
- The remaining destructuring boundary is more precise: declaration driver
  shapes and descriptor-backed assignment destructuring are separate lanes, and
  unsupported neighbors remain pre-VM declines.
- Future binding-target widening must keep parity tests for stack shape,
  observable target writes, abrupt completion, and name-inference exclusions.

## Evidence

- Delivery PR #2941 merged commit `c38215edb73bc6ca5c394e9fe11e70ac60c79956`.
- Build-stage verification recorded:
  - `rtk git diff --check main...HEAD`
  - no matches from the focused AST seam scan
  - focused destructuring pack passed with 3 tests
  - production eligibility/invocation pack passed with 605 tests
- Review-requested regression coverage proved
  `AssignmentDestructuringValue_DoesNotInferBindingTargetName_OnFastPath`.

## Related

- `docs/unified-bytecode-expansion-contract.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.BindingTargetPrograms.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- ADR 0251: `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- ADR 0284: `docs/adrs/0284-keep-unified-bytecode-object-destructuring-model-first-and-static-key-owned.md`
