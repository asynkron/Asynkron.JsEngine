# Activation No-Args Literal Return Fast Path

Date: 2026-05-26

## Profile

Selected profile: `activation-noargs-lite`

Pre-edit benchmark sample:

```text
activation-noargs-lite          634      130  Jint 4.88x faster
```

Pre-edit CPU profile pointed at sync IR trampoline frame setup under no-argument
calls:

```text
TypedAstEvaluator.SyncFunctionInvoker.SyncIrCallTrampoline.PushFrame
Buffer.BulkMoveWithWriteBarrier
```

## Change

Functions whose lowered IR is a simple literal return now cache that shape on
`ExecutionPlan`. `SyncFunctionInvoker` can return the literal directly for safe
simple sync functions, before renting a full invocation context or allocating
sync IR trampoline frames. The existing trampoline still has a guarded literal
return fallback for call sites that reach it.

This follow-up also adds one guarded no-argument parameter-return shape:
`return a;` may return `undefined` directly when the lowered plan proves the
return expression is a single parameter-slot load and the call has zero
arguments.

The fast path is limited to simple, non-constructor, non-async, non-generator
functions with the existing simple IR activation plan shape and without dynamic
activation features such as `arguments`, captured activation environments,
private/super state, or instance fields.

## Evidence

Focused proof pack:

```text
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ActivationSemanticsProofPackTests|FullyQualifiedName~ExecutionPlanDiagnosticsTests.FunctionPlan_SimpleReturnExpression_IsMarkedAsIrCallShape"
ok dotnet test: 33 tests passed
```

Repeated selected-profile measurements after the initial literal-return slice:

```text
activation-noargs-lite          276      270  Tie
activation-noargs-lite          262      287  Tie
activation-noargs-lite          265      128  Jint 2.07x faster
```

Compared with the 634 ms pre-edit Asynkron baseline, the repeated post-edit
Asynkron samples are about 56-59% faster.

gh2040 widening evidence (guarded no-argument parameter-return shape):

```text
baseline (9a3ec1ed^): activation-noargs-lite          267      140  Jint 1.91x faster
final (9a3ec1ed):     activation-noargs-lite          258      130  Jint 1.98x faster
```

This widening sample pair is effectively a no-regression confirmation for the
selected profile, which still primarily exercises the literal-return body.
