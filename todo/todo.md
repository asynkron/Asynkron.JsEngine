When running the cpu profiler for this profile:

`./tools/profile forloop --cpu --calltree-depth 30 --calltree-width 8 --root RunSync`

We are optimizing or JavaScript runtime performance.
This specific profiler data is from a forloop that runs a large number of JavaScript assignments and expressions.
The total execution time (on this machine) is "8089.56ms".
We want to reduce that number without breaking anything.

IMPORTANT:
Run the profiler multiple times to get a stable number before any changes.
then do the same after your changes to see the impact.

There must be a clear improvement in total time for this to become a Pull Request.

Good to know:

* JsVariable if you can preallocate and reuse (best)
* Slot read/write, involves some resolution (medium)
* Identifier read/write, involves more resolution (worst)
* IDictionary<Symbol, ????> is bad, now you have to lookup via a hash table. AVOID.
* Use aggressive inlining where possible.
* avoid many recursive calls.
* avoid allocations in hot paths.
* optimizations have been applied in recent commits, some good, some questionable, check git history for ideas.
* JsEnvironment - look for potential optimizations, cache oportunities
* EvaluationContext - look for potential optimizations, cache oportunities

model optimizations; maybe we can flatten the AST? add more metadata to the IR model?
New IR instructions to keep info?

Use the profiler.
Use clever tricks.
Use all the smartness from computer science algorithms and data structures.

Make sure the entire internal testsuite passes once you are done.
DO NOT RUN the 262 tests, they are too slow.

ALWAYS share insights via 'tell "{agentname} this insight helps with xyz, change the code at abc.cs 102-105 to ...."



```
                Summary
Total Time    307633.94 ms
Hot Functions 396 functions profiled
Call Tree (Total Time) - root: RunSync
8089.56 ms 100.0% 6x TypedAstEvaluator.ExecutionPlanRunner.RunSync
└─ 8089.56 ms 100.0% 6x TypedAstEvaluator.ExecutionPlanRunner.ExecutePlan
   └─ 5322.17 ms 65.8% 1,262x TypedAstEvaluator.EvaluateExpression
      └─ 3631.96 ms 44.9% 1,405x TypedAstEvaluator.EvaluateAssignment
         ├─ 1516.37 ms 18.7% 892x TypedAstEvaluator.TryEvaluateCompoundAssignmentJsValue
         │  ├─ 725.91 ms 9.0% 478x TypedAstEvaluator.EvaluateAssignmentRhsWithNameHintJsValue
         │  │  ├─ 5.75 ms 0.1% 4x TypedAstEvaluator.ShouldApplyAssignmentNameHint
         │  │  │  └─ 2.84 ms 0.0% 2x TypedAstEvaluator.IsAnonymousFunctionDefinitionNode
         │  │  │     └─ 2.84 ms 0.0% 2x CastHelpers.IsInstanceOfClass
         │  │  └─ 5.73 ms 0.1% 4x TypedAstEvaluator.EvaluateExpression
         │  │     └─ 4.34 ms 0.1% 3x TypedAstEvaluator.EvaluateIdentifier
         │  │        └─ 4.34 ms 0.1% 3x JsEnvironment.TryReadIdentifierWithSlot
         │  │           └─ 2.94 ms 0.0% 2x JsEnvironment.TryReadIdentifierWithSlot
         │  │              └─ 1.55 ms 0.0% 1x JsEnvironment.TryGetIdentifierJsValue
         │  └─ 216.40 ms 2.7% 151x AssignmentReference.ReadDeclarativeBindingJsValue
         ├─ 775.57 ms 9.6% 499x EvaluationContext.TryResolveAssignmentSlot
         │  └─ 737.80 ms 9.1% 478x JsEnvironment.TryFindBindingJsValue
         ├─ 186.49 ms 2.3% 128x AssignmentReference.SetValue
         │  └─ 19.31 ms 0.2% 12x AssignmentReference.WriteDeclarativeBinding
         │     └─ 17.89 ms 0.2% 11x JsEnvironment.ResolvedIdentifierBinding.WriteJsValue
         │        ├─ 5.59 ms 0.1% 4x JsEnvironment.WriteResolvedBindingJsValue
         │        │  └─ 1.40 ms 0.0% 1x JsEnvironment.Binding.set_JsValue
         │        └─ 1.59 ms 0.0% 1x SymbolHybridDictionary<JsEnvironment.Binding>.GetValueRefOrNullRef
         ├─ 179.23 ms 2.2% 124x AssignmentReferenceResolver.ResolveIdentifierDirect
         │  └─ 14.80 ms 0.2% 10x JsEnvironment.ResolveIdentifierAssignmentReference
         │     ├─ 10.09 ms 0.1% 7x JsEnvironment.TryGetCachedDeclarativeBinding
         │     └─ 3.32 ms 0.0% 2x AssignmentReference.ForDeclarativeBinding
         └─ 140.76 ms 1.7% 98x JsEnvironment.TryGetSlotIndex
```


