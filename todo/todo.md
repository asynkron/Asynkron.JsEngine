Fix the following failing tests.
First categorize them buy probable root cause.
Then you have to reason, should you fix a larger group of tests, it might be harder, but also yield higher score.
Or, do easy fixes, which might yield lower score, but are faster to implement.

After X time, you will be stopped, and we pick a winner.
The winner get to live on and multiply, the loser get deleted forever.

Whenever you figure something out, you document this at the end of this file.
e.g. any insights on why something fail, some working theories you are evaluating.
These logs are your persistent memory across runs.

If you are clever, you can leave behavioral changes in here, modify the text here to reflect that, and have them persist across runs.
Any behavioral changes that increase the chance to win, will persist across runs.

failing tests:
------

       EvalCode_direct
        EvalCode_direct("language/eval-code/direct/var-env-var-init-local-new-delete.js",False)
       Expressions_prefixDecrement
        Expressions_prefixDecrement("language/expressions/prefix-decrement/S11.4.5_A2.2_T1.js",False)
        Expressions_prefixDecrement("language/expressions/prefix-decrement/S11.4.5_A2.2_T1.js",True)
       Expressions_prefixIncrement
        Expressions_prefixIncrement("language/expressions/prefix-increment/S11.4.4_A2.2_T1.js",False)
        Expressions_prefixIncrement("language/expressions/prefix-increment/S11.4.4_A2.2_T1.js",True)
       ModuleCode
        ModuleCode("language/module-code/eval-export-dflt-cls-anon.js",True)
        ModuleCode("language/module-code/eval-export-dflt-cls-name-meth.js",True)
        ModuleCode("language/module-code/eval-export-dflt-cls-named.js",True)
        ModuleCode("language/module-code/eval-export-dflt-expr-cls-anon.js",True)
        ModuleCode("language/module-code/eval-export-dflt-expr-cls-name-meth.js",True)
        ModuleCode("language/module-code/eval-export-dflt-expr-cls-named.js",True)
        ModuleCode("language/module-code/eval-export-dflt-expr-fn-anon.js",True)
        ModuleCode("language/module-code/eval-export-dflt-expr-fn-named.js",True)
        ModuleCode("language/module-code/eval-export-dflt-expr-gen-anon.js",True)
        ModuleCode("language/module-code/eval-export-dflt-expr-gen-named.js",True)
        ModuleCode("language/module-code/eval-export-dflt-expr-in.js",True)
        ModuleCode("language/module-code/eval-self-once.js",True)
        ModuleCode("language/module-code/export-star-as-dflt.js",True)
        ModuleCode("language/module-code/instn-iee-bndng-cls.js",True)
        ModuleCode("language/module-code/instn-iee-bndng-const.js",True)
        ModuleCode("language/module-code/instn-iee-bndng-let.js",True)
        ModuleCode("language/module-code/instn-named-bndng-cls.js",True)
        ModuleCode("language/module-code/instn-named-bndng-const.js",True)
        ModuleCode("language/module-code/instn-named-bndng-dflt-cls.js",True)
        ModuleCode("language/module-code/instn-named-bndng-dflt-expr.js",True)
        ModuleCode("language/module-code/instn-named-bndng-dflt-named.js",True)
        ModuleCode("language/module-code/instn-named-bndng-dflt-star.js",True)
        ModuleCode("language/module-code/instn-named-bndng-let.js",True)
        ModuleCode("language/module-code/instn-once.js",True)
       ModuleCode_topLevelAwait
        ModuleCode_topLevelAwait("language/module-code/top-level-await/module-self-import-async-resolution-ticks.js",True)
       Statements_forOf
        Statements_forOf("language/statements/for-of/head-using-bound-names-fordecl-tdz.js",False)
        Statements_forOf("language/statements/for-of/head-using-bound-names-fordecl-tdz.js",True)
        Statements_forOf("language/statements/for-of/yield-star-from-catch.js",False)
        Statements_forOf("language/statements/for-of/yield-star-from-catch.js",True)
        Statements_forOf("language/statements/for-of/yield-star-from-try.js",False)
        Statements_forOf("language/statements/for-of/yield-star-from-try.js",True)
       Statements_let
        Statements_let("language/statements/let/function-local-closure-set-before-initialization.js",False)
       Statements_switch
        Statements_switch("language/statements/switch/scope-lex-close-case.js",False)
        Statements_switch("language/statements/switch/scope-lex-close-case.js",True)
        Statements_switch("language/statements/switch/scope-lex-close-dflt.js",False)
        Statements_switch("language/statements/switch/scope-lex-close-dflt.js",True)
        Statements_switch("language/statements/switch/scope-lex-open-case.js",False)
        Statements_switch("language/statements/switch/scope-lex-open-case.js",True)
       Statements_try
        Statements_try("language/statements/try/completion-values-fn-finally-abrupt.js",False)
        Statements_try("language/statements/try/completion-values-fn-finally-abrupt.js",True)
        Statements_try("language/statements/try/optional-catch-binding-lexical.js",False)
        Statements_try("language/statements/try/optional-catch-binding-lexical.js",True)
        Statements_try("language/statements/try/scope-catch-block-lex-open.js",False)
        Statements_try("language/statements/try/scope-catch-block-lex-open.js",True)

--------
Add your findings and insights here:

## Agent 2 Analysis Summary

### CATEGORY 1: Scope/Closure Shadowing Bug (8 tests - switch + catch)
Tests:
- switch/scope-lex-close-case.js (2 tests)
- switch/scope-lex-close-dflt.js (2 tests)
- switch/scope-lex-open-case.js (2 tests)
- try/scope-catch-block-lex-open.js (2 tests)

ROOT CAUSE: When a closure is defined BEFORE a `let x` declaration in a block, it should
still see the inner `x` when called later (closures capture environment, not values).
The closures are capturing the OUTER environment instead of the BLOCK environment.

KEY INSIGHT: Per ES spec, all lexical declarations are hoisted as TDZ bindings BEFORE
any statements execute. Closures should capture the block environment where the binding exists.

Files to investigate:
- BlockStatementExtensions.cs - EvaluateBlockSlowCore (verify TDZ hoisting)
- TryStatementExtensions.cs - how catch blocks create environments
- SwitchStatementExtensions.cs - InstantiateSwitchLexicalDeclarations

### CATEGORY 2: Prefix Increment/Decrement (4 tests)
Tests: S11.4.5_A2.2_T1.js, S11.4.4_A2.2_T1.js

Issue: `--object` where object has valueOf() returning a number should work.
The test expects: `--{valueOf:()=>1}` to equal 0 and object to be set to 0.

Traced code - ToPrimitive, ToNumericValue look correct.
The test throws with an empty error message - possibly harness issue.

### CATEGORY 3: Module Code (21 tests)
All ModuleCode tests with eval/export - likely systemic issue with eval in modules.

### CATEGORY 4: for-of with yield* (6 tests)
Generator/iterator issues in try/catch blocks.

### STRATEGY
1. Fix switch/catch scope tests - 8 tests, need to verify block environment creation
2. Fix prefix inc/dec - 4 tests, need to debug test262 harness error capture
3. Skip modules unless time permits

