When running the memory profiler for this profile:

`./tools/profile forloop --memory --calltree-depth 4 --calltree-width 8`

It shows significant memory allocations for `<>c__DisplayClass0_0` 48.06 MB.

This is because the for loop and body are incorrectly falling back to dictionary lookup for variables.
Each variable in there should have a scope/environment, and slot-id´s assigned.

```
Allocation Call Tree (Sampled)
<>c__DisplayClass0_0 (48.06 MB, 87.9%, 473x)
48.06 MB 100.0% 473x <>c__DisplayClass0_0
└─ 48.06 MB 100.0% 473x AssignmentReferenceResolver.ResolveIdentifierDirect
└─ 48.06 MB 100.0% 473x TypedAstEvaluator.EvaluateAssignment
└─ 48.06 MB 100.0% 473x TypedAstEvaluator.EvaluateExpression
└─ 48.06 MB 100.0% 473x TypedAstEvaluator.ExecutionPlanRunner.ExecutePlan
JsValue[] (2.52 MB, 4.6%, 2x)
2.52 MB 100.0% 2x JsValue[]
├─ 2.29 MB 90.7% 1x JsValue.CreateIntegerCache
│  └─ 2.29 MB 90.7% 1x JsValue.cctor
│     └─ 2.29 MB 90.7% 1x InitHelpers.InitClassSlow
│        └─ 2.29 MB 90.7% 1x StaticsHelpers.GetGCStaticBaseSlow
└─ 240.02 KB 9.3% 1x JsValueCache.cctor
└─ 240.02 KB 9.3% 1x InitHelpers.InitClassSlow
└─ 240.02 KB 9.3% 1x StaticsHelpers.GetGCStaticBaseSlow
└─ 240.02 KB 9.3% 1x JsValueCache.GetNumber
```

Your job is to figure out why this is happening, make a plan, use test bombs, use layered tests.
