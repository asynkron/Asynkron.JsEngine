When running the cpu profiler for this profile:

`./tools/profile forloop --cpu --calltree-depth 30 --calltree-width 8 --root RunSync`

We are optimizing or JavaScript runtime performance.
This specific profiler data is from a forloop that runs a large number of JavaScript assignments and expressions.
The total execution time (on this machine) is "7816.91 ms".
We want to reduce that number without breaking anything.

One thing that we have not yet done here is the use of "JsVariable" for local variables.
JsVariable is a resolved slot, which you can assign to, without re-resolving the slot.
This MIGHT be something that helps.
in a recent commit we introduced re-use of JsEnvironment for per iteration blocks if no closures are involved.
In this special case, we could likely resolve JsVariables ONCE, as they are already bound to the right slots.

It should likely get rid of "ResolveIdentifierDirect", assuming we resolve these JsVariables up front.

Use the profiler.
Use clever tricks.
Use all the smartness from computer science algorithms and data structures.

Make sure the entire internal testsuite passes once you are done.
DO NOT RUN the 262 tests, they are too slow.


```
                Summary
Total Time    296603.54 ms
Hot Functions 397 functions profiled
Call Tree (Total Time) - root: RunSync
7816.91 ms 100.0% 4x TypedAstEvaluator.ExecutionPlanRunner.RunSync
└─ 7816.91 ms 100.0% 4x TypedAstEvaluator.ExecutionPlanRunner.ExecutePlan
   └─ 4952.16 ms 63.4% 1,281x TypedAstEvaluator.EvaluateExpression
      ├─ 3313.27 ms 42.4% 1,346x TypedAstEvaluator.EvaluateAssignment
      │  ├─ 1754.82 ms 22.4% 960x TypedAstEvaluator.TryEvaluateCompoundAssignmentJsValue
      │  │  ├─ 771.36 ms 9.9% 488x TypedAstEvaluator.EvaluateAssignmentRhsWithNameHintJsValue
      │  │  │  ├─ 10.04 ms 0.1% 6x TypedAstEvaluator.EvaluateExpression
      │  │  │  │  └─ 5.57 ms 0.1% 3x TypedAstEvaluator.EvaluateIdentifier
      │  │  │  │     └─ 4.20 ms 0.1% 3x JsEnvironment.TryReadIdentifierWithSlot
      │  │  │  │        └─ 1.40 ms 0.0% 1x JsEnvironment.TryReadIdentifierWithSlot
      │  │  │  ├─ 8.74 ms 0.1% 6x TypedAstEvaluator.ShouldApplyAssignmentNameHint
      │  │  │  │  └─ 2.81 ms 0.0% 2x TypedAstEvaluator.IsAnonymousFunctionDefinitionNode
      │  │  │  │     └─ 1.42 ms 0.0% 1x CastHelpers.IsInstanceOfClass
      │  │  │  └─ 4.41 ms 0.1% 3x CastHelpers.IsInstanceOfInterface
      │  │  ├─ 255.03 ms 3.3% 176x AssignmentReference.ReadDeclarativeBindingJsValue
      │  │  └─ 44.78 ms 0.6% 32x TypedAstEvaluator.AddValue
      │  ├─ 485.77 ms 6.2% 318x AssignmentReferenceResolver.ResolveIdentifierDirect
      │  │  ├─ 196.20 ms 2.5% 134x JsEnvironment.TryResolveWithBinding
      │  │  │  └─ 1.37 ms 0.0% 1x SymbolHybridDictionary<JsEnvironment.Binding>.ContainsKey
      │  │  └─ 17.23 ms 0.2% 9x JsEnvironment.ResolveIdentifierAssignmentReference
      │  │     ├─ 9.75 ms 0.1% 4x JsEnvironment.TryGetCachedDeclarativeBinding
      │  │     └─ 4.65 ms 0.1% 3x AssignmentReference.ForDeclarativeBinding
      │  ├─ 159.19 ms 2.0% 113x AssignmentReference.SetValue
      │  │  └─ 5.81 ms 0.1% 4x AssignmentReference.WriteDeclarativeBinding
      │  │     └─ 5.81 ms 0.1% 4x JsEnvironment.ResolvedIdentifierBinding.WriteJsValue
      │  │        ├─ 1.45 ms 0.0% 1x JsEnvironment.WriteResolvedBindingJsValue
      │  │        └─ 1.38 ms 0.0% 1x SymbolHybridDictionary<JsEnvironment.Binding>.GetValueRefOrNullRef
      │  └─ 133.50 ms 1.7% 94x JsEnvironment.TryGetSlotIndex
      └─ 170.90 ms 2.2% 117x TypedAstEvaluator.EvaluateExpression
```


