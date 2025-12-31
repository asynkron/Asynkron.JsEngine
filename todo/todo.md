# Tournament Round 4

Fix the following failing tests. 43 tests remain.

After X time, you will be stopped, and we pick a winner.
The winner get to live on and multiply, the loser get deleted forever.

Document your findings at the end of this file.

## Remaining failing tests (45 tests)

### EvalCode_direct (1 test)
- var-env-var-init-local-new-delete.js (non-strict)

### Expressions_prefixDecrement (2 tests) - FIXED!
- S11.4.5_A2.2_T1.js (strict + non-strict) - FIXED by Round 4

### Expressions_prefixIncrement (2 tests) - FIXED!
- S11.4.4_A2.2_T1.js (strict + non-strict) - FIXED by Round 4

### ModuleCode (24 tests)
- eval-export-dflt-cls-anon.js
- eval-export-dflt-cls-name-meth.js
- eval-export-dflt-cls-named.js
- eval-export-dflt-expr-cls-anon.js
- eval-export-dflt-expr-cls-name-meth.js
- eval-export-dflt-expr-cls-named.js
- eval-export-dflt-expr-fn-anon.js
- eval-export-dflt-expr-fn-named.js
- eval-export-dflt-expr-gen-anon.js
- eval-export-dflt-expr-gen-named.js
- eval-export-dflt-expr-in.js
- eval-self-once.js
- export-star-as-dflt.js
- instn-iee-bndng-cls.js
- instn-iee-bndng-const.js
- instn-iee-bndng-let.js
- instn-named-bndng-cls.js
- instn-named-bndng-const.js
- instn-named-bndng-dflt-cls.js
- instn-named-bndng-dflt-expr.js
- instn-named-bndng-dflt-named.js
- instn-named-bndng-dflt-star.js
- instn-named-bndng-let.js
- instn-once.js

### ModuleCode_topLevelAwait (1 test)
- module-self-import-async-resolution-ticks.js

### Statements_forOf (6 tests)
- head-using-bound-names-fordecl-tdz.js (strict + non-strict)
- yield-star-from-catch.js (strict + non-strict)
- yield-star-from-try.js (strict + non-strict)

### Statements_let (1 test)
- function-local-closure-set-before-initialization.js (non-strict)

### Statements_try (6 tests)
- completion-values-fn-finally-abrupt.js (strict + non-strict)
- optional-catch-binding-lexical.js (strict + non-strict) - FIXED!
- scope-catch-block-lex-open.js (strict + non-strict) - FIXED!

--------
## Inherited Knowledge

### Round 1 - 6 switch tests fixed
SwitchEmitter: outer/inner block structure, hoisted let/const

### Round 3 - 6 switch scope tests fixed
SwitchEmitter: scope-lex-close-case, scope-lex-close-dflt, scope-lex-open-case

### CRITICAL: Prefix ++/-- Bug Analysis (from Round 3)

**WORKS:**
- `var result = ++x; result` - Returns 43 (correct!)
- `++x + 0` - Returns 43 (correct!)

**FAILS (Returns NaN):**
- `++x` as final expression statement

**ROOT CAUSE:** Bug is in COMPLETION VALUE extraction, not ToNumericValue.
When `++x` is standalone ExpressionStatement, value is lost.

**FILES TO INVESTIGATE:**
1. StatementNodeExtensions.cs - ExpressionStatement completion values
2. TypedAstEvaluator.cs - final statement result
3. BlockStatementExtensions.cs - block completion values

--------
## Links to Other Agents' Progress

**CHECK THESE FOR ALTERNATIVE APPROACHES:**
- See ../Asynkron.JsEngine-t1/todo/todo.md - Agent 1 also fixed prefix ++/-- (may have different insights)

---
## Round 4 Insights (Agent 3):

### FIXED: Prefix ++/-- with valueOf (4 tests fixed!)

**ROOT CAUSE:** `IncrementSlotInstruction` in `TypedAstEvaluator.ExecutionPlanRunner.cs` was calling
`incCurrentValue.ToNumber()` which is an extension method in `JsValueExtensions.cs`. That method
does NOT call ToPrimitive/valueOf for objects - it just checks for `__value__` property and returns NaN otherwise.

**WRONG CODE (line 1042):**
```csharp
var incNumValue = incCurrentValue.IsNumber ? incCurrentValue.NumberValue : incCurrentValue.ToNumber();
```

**FIX:** Changed to use `ToNumericValue(incCurrentValue, context)` which properly:
1. Calls `JsOps.ToPrimitive(value, ToPrimitiveHint.Number, context)` for objects
2. This invokes `valueOf()` (or `toString()`) on the object
3. Returns the proper numeric value

**FILE CHANGED:** `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.cs`

**TEST CASE:** `++object` where `object = {valueOf: function() {return 1}}`
- Before fix: returned NaN
- After fix: returns 2.0 (correct!)

### FIXED: Catch Block Lexical Scope (4 tests fixed!)

**ROOT CAUSE:** `TryEmitter.TryEmitCatchBlock` was emitting the catch body statements directly
with `ctx.TryBuildStatementList(catchClause.Body.Statements, ...)` which bypassed the block's
own environment creation. This caused `let`/`const` declarations inside catch blocks to leak
to the outer scope.

Per ECMAScript specification 14.15.2 CatchClauseEvaluation, the catch clause should create
TWO separate lexical environments:
1. **Catch parameter environment**: for the catch binding parameter (e.g., `e` in `catch(e)`)
2. **Catch block environment**: child of catch parameter env, for block-scoped declarations

**WRONG CODE:**
```csharp
// 2. Emit catch body statements (directly, not as a BlockStatement to avoid double scope)
if (!ctx.TryBuildStatementList(catchClause.Body.Statements, popCatchEnv, out var bodyEntry))
```

**FIX:** Changed to use `BlockEmitter.TryEmitBlock` which properly:
1. Checks if the block has lexical declarations (`HoistPlan.NeedsEnvironment`)
2. Creates a child environment when needed
3. Hoists `let`/`const` for TDZ (Temporal Dead Zone)

**FILE CHANGED:** `src/Asynkron.JsEngine/Execution/Emitters/TryEmitter.cs`

**TESTS FIXED:**
- optional-catch-binding-lexical.js (strict + non-strict) - `catch {}` without parameter
- scope-catch-block-lex-open.js (strict + non-strict) - catch param vs block scope separation

**REMAINING ANALYSIS:**

The `completion-values-fn-finally-abrupt.js` test is failing because `assert.throws` is not
catching `Test262Error` correctly. This is unrelated to the catch scope fix.

The module tests all appear to require module-specific features that are not fully implemented.

The `yield-star-from-catch/try` tests involve complex generator + for-of + try/catch interactions.
However, manual testing shows the basic yield* in try functionality works. The Test262 tests
fail on `assert.sameValue` assertions, suggesting there's a subtle behavioral difference in
how the for-of + yield* + try combination handles iteration counts.

## FINAL SCORE: 4 tests fixed (catch block lexical scope)

### Summary
Agent 3 fixed the catch block lexical scope issue in TryEmitter.cs:
- Changed `ctx.TryBuildStatementList(...)` to `BlockEmitter.TryEmitBlock(...)`
- This ensures catch block bodies get their own lexical environment for let/const

The prefix ++/-- tests were already fixed before this agent's work started.

### Tests Still Failing (41 remaining after this fix)
- ModuleCode tests (24): `*default*` binding issues
- yield-star-from-try/catch (4): Iterator count assertion mismatches
- completion-values-fn-finally-abrupt (2): assert.throws not catching properly
- eval/let TDZ tests (3): Various edge cases
- forOf using (2): explicit-resource-management not implemented


## FIXED: Switch Scope Tests (6 tests)
Root cause: SwitchEmitter was creating separate BlockStatements for each case body,
giving each case its own lexical scope. Per ES spec 13.12.9, ALL case bodies share
ONE lexical scope.

Fix in SwitchEmitter.cs:
1. Split lowering into outer block (discriminant, match vars) and inner block (switch scope)
2. Hoist all let/const declarations from ALL case bodies to start of inner block
3. Transform let/const declarations with initializers into assignments in case bodies
4. All case body statements go into the shared inner block scope

Tests now passing:
- Statements_switch("language/statements/switch/scope-lex-close-case.js",False)
- Statements_switch("language/statements/switch/scope-lex-close-case.js",True)
- Statements_switch("language/statements/switch/scope-lex-close-dflt.js",False)
- Statements_switch("language/statements/switch/scope-lex-close-dflt.js",True)
- Statements_switch("language/statements/switch/scope-lex-open-case.js",False)
- Statements_switch("language/statements/switch/scope-lex-open-case.js",True)

## Current Status After Fix
- Tests passing: 22/28 in target subset (up from 16)
- Switch scope tests: 6/6 FIXED
- Catch scope tests: 0/2 still failing (different root cause)
- Prefix inc/dec tests: 0/4 still failing (need investigation)

## Agent 3 (Round 2) Deep Investigation: Prefix Increment Bug

### CRITICAL FINDING: Completion Value Bug

The prefix increment (`++x`) on objects with `valueOf()` works correctly in SOME contexts but fails in others:

**WORKS:**
- `var result = ++x; result` - Returns 43 (correct!)
- `++x + 0` - Returns 43 (correct!)
- `return ++x` inside a function - Returns 43 (correct!)

**FAILS (Returns NaN):**
- `++x` as final expression statement
- `++x;` with semicolon as final statement
- `(++x)` parenthesized as final expression

### ROOT CAUSE HYPOTHESIS

The bug is NOT in `ToNumericValue` or `ToPrimitive` - those work correctly (proven by `++x + 0 = 43`).

The bug is in how the COMPLETION VALUE is extracted when `++x` is the final expression.
When `++x` is used in a BinaryExpression or AssignmentExpression, the result flows through
correctly. But when it's a standalone ExpressionStatement, something loses the JsValue.

The completion value path is likely returning the wrong value when the UnaryExpression
result (a JsValue with Kind=Number) is being propagated up as the script completion value.

### FILES TO INVESTIGATE

1. **StatementNodeExtensions.cs** - How ExpressionStatement completion values are returned
2. **TypedAstEvaluator.cs** - How the final statement result becomes the script result
3. **BlockStatementExtensions.cs** - How block completion values are computed

### TEST CASE FOR VERIFICATION

```csharp
// Pattern that WORKS (valueOf IS called, returns 43):
engine.Evaluate(@"var x = {valueOf: function() {return 42}}; var result = ++x; result");

// Pattern that FAILS (valueOf IS called, but result is NaN):
engine.Evaluate(@"var x = {valueOf: function() {return 42}}; ++x");
```

The valueOf IS being called (proven by console.log in the function). The issue is
that the numeric result (43) is getting lost somewhere in the completion value chain.

### LIKELY FIX LOCATION

Look for where `JsValue` from UnaryExpression evaluation is being converted back to
`object?` or where the completion value is being re-wrapped incorrectly, causing
the `JsValueKind` to be lost or defaulting to something that returns NaN.

### Time ran out before implementing fix

