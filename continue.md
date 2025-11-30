# Language Suite Next Steps

## Current State
- Direct eval inside class field initializers now inherits the enclosing `super` binding even when the base prototype lacks the requested property: `super.prop` now returns `undefined` rather than throwing, and regression tests cover eval-produced arrow functions assigning fields.
- Activity-based regression tests exist for the failing `Expressions_class_elements` shapes (instance/static/eval arrow cases) so we can iterate without running the full Test262 language suite every time.
- Generator functions now preserve previously supplied resume values when replaying class member computed names, so `yield` inside class accessor names works in both the parser (computed `in` expressions) and the runtime (see the updated `ClassComputedAccessorTests`). The Test262 suites still contain numerous failures (e.g., statement forms hit generator IR gaps and static/private accessor cases fail earlier in evaluation), but the new infrastructure unblocks further fixes.
- Class expressions whose binding names spell `await` (including escape sequences) now parse correctly in strict script goals; the `class-name-ident-await*.js` Test262 cases both pass.
- Generator iterator objects inherit from their function prototype (or `Object.prototype` fallback) so computed property names that call generator functions convert to property keys instead of throwing `TypeError: Cannot convert object to primitive value`.

## Next Iteration Plan
1. **Triage the remaining class element failures** – focus on eval/new.target guardrails, private accessor name parsing, and the Annex B “contains supercall” buckets. Use the activity tracer to capture the exact scope stack for one failing `Expressions_class_elements` test and port that insight into targeted unit tests before touching the evaluator.
2. **Align compound assignment semantics with spec** – ensure property references are resolved exactly once, nullish bases throw `TypeError` before the RHS executes, and `with`/proxy/@@unscopables paths reuse the cached reference. Add regression tests mirroring the `language/expressions/compound-assignment/*` cases called out in `failing/languagetests.testsession`.
3. **Audit parser/evaluator helpers shared between eval + class fields** – in particular the `EvaluatePropertyAssignment` / `AssignmentReferenceResolver` paths – so private names, `arguments`, and `new.target` behave the same whether the initializer is literal or produced by direct/indirect eval.
