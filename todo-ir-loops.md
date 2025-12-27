# Environment Creation Timing: IR vs AST Path

## ECMAScript Specification (ForBodyEvaluation)

According to the spec, the order is:
1. **CreatePerIterationEnvironment** (before loop starts)
2. **Repeat:**
   - Evaluate condition
   - Evaluate body
   - **CreatePerIterationEnvironment** ← AFTER body, BEFORE increment
   - Evaluate increment

## AST Path (LoopPlanExtensions.cs lines 233-244)

```
Condition → Body → CreateNextIterationEnvironment → Increment → (repeat)
```

The AST path **follows the spec**: environment is created AFTER body, BEFORE increment.

## IR Path (ExecutionPlanBuilder.cs lines 639-649)

```
Condition → CreateIterationEnvironmentInstruction → Body → Increment → (repeat)
```

The IR path creates the environment **BEFORE the body** (immediately after condition check).

## The Difference

| Path | Order |
|------|-------|
| **AST (spec-compliant)** | Condition → Body → **CreateEnv** → Increment |
| **IR** | Condition → **CreateEnv** → Body → Increment |

## Why This Matters

The increment expression modifies the loop variable in different environments:

- **AST/Spec**: Increment writes to the **newly created** environment (created after body)
- **IR**: Increment writes to the **same** environment where the body executed

For closures that capture loop variables, this could cause observable differences in behavior when the closure is invoked later - the captured value may differ depending on whether it was captured before or after the increment wrote to that specific environment.

## Files

- **AST path**: `src/Asynkron.JsEngine/Ast/LoopPlanExtensions.cs` lines 233-244
- **IR builder**: `src/Asynkron.JsEngine/Execution/ExecutionPlanBuilder.cs` lines 618-649
- **IR executor**: `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.cs` lines 929-1054
