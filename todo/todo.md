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
- optional-catch-binding-lexical.js (strict + non-strict)
- scope-catch-block-lex-open.js (strict + non-strict)

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


