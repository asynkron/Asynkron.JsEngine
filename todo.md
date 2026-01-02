There are a few test failures right now:

-----
     EnvironmentPoolingTests
      ForOfLoop_WithClosureInsideBody_ClosuresCaptureEnvironments
      LabeledLoop_WithBreak_EnvironmentsReturned
      NestedForOfLoops_IndependentPooling
     SlotStampingTests
      ForAwait_PerIteration_Bindings_Are_Stamped_With_Slots

-----


These are the result of our recent work on "slot stamping", we are trying to get our scope and slot analyzis to work and correctly stamp slots on identifiers.
So that they can resolve variables in their correct scope without having to do a full lookup.

Aim to get to 0 errors.

1. You may not replace IR with AST evaluation.
2. you may not disable tests.
3. You may not disable slot stamping.
4. You may not disable optimizations.

If you need to make a plan first, do that, its OK.
If you need more IR instructions, add that, its OK.
If you need to change existing IR instructions, do that, its OK.
If you need to change the slot stamping logic, do that, its OK.
If you need to change the evaluator logic, do that, its OK.
