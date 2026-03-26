# Investigation Report: Strictness Propagation Bug

## Problem Summary
~20 Test262 tests fail because sloppy-mode code is incorrectly treated as strict. Symptoms include: arrow functions in sloppy mode throwing ReferenceError on implicit globals, `delete arguments[0]` throwing TypeError instead of returning `false`, and arrow functions not inheriting `arguments` from enclosing scope.

## Affected Components

### Parser
- `src/Asynkron.JsEngine/Parser/JsAstParser.cs` -- `CheckForUseStrictDirective()` (line 4427), `ParseBlock()` (line 1591), `ParseArrowFunctionBody()` (line 4120), `ParseFunctionTail()` (line 3272)

### AST
- `src/Asynkron.JsEngine/Ast/FunctionExpression.cs` -- `IsArrow` flag, `Body.IsStrict` as strictness source
- `src/Asynkron.JsEngine/Ast/BlockStatement.cs` -- `IsStrict` property (line 13)
- `src/Asynkron.JsEngine/Ast/PropertyHandle.cs` -- `Delete()` method (line 121), `_isStrict` field

### Runtime / IR
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Core.cs` -- `_isStrict` initialization (line 52)
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Environment.cs` -- environment setup, arguments object creation (lines 74, 163, 261)
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Helpers.cs` -- delete handlers (lines 1279-1299)
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs` -- arguments handling (lines 117, 164-165, 1222)
- `src/Asynkron.JsEngine/Ast/FunctionExpressionExtensions.cs` -- `CreateArgumentsObject()` (line 15)
- `src/Asynkron.JsEngine/Ast/AssignmentReference.cs` -- `WriteUnresolvable()` (line 321)
- `src/Asynkron.JsEngine/JsEnvironment.cs` -- `AssignUnresolvableJsValue()` (line 2002), `IsStrict` (line 183)

## Evidence Collected

### Test Output

```
Failed ArgumentsObject_mapped("language/arguments-object/mapped/mapped-arguments-nonconfigurable-delete-1.js",False)
Error Message:
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Cannot delete property'
```

The test runs with `strict=False` (sloppy mode). The test code:
```js
function argumentsAndDelete(a) {
  Object.defineProperty(arguments, "0", {configurable: false});
  assert.sameValue(delete arguments[0], false);  // <-- should return false, not throw
  assert.sameValue(a, 1);
  assert.sameValue(arguments[0], 1);
}
argumentsAndDelete(1);
```

Expected: `delete arguments[0]` returns `false` in sloppy mode.
Actual: Throws `TypeError: Cannot delete property` (strict-mode behavior).

### Code Analysis

#### Strictness Chain
The strictness of a function at runtime is determined by:
```csharp
// ExecutionPlanRunner.Core.cs:52
_isStrict = function.Body.IsStrict || closure.IsStrict || isLexicallyStrict;
```

This `_isStrict` is then used to:
1. Create all function environments (ExecutionPlanRunner.Environment.cs:74, 78, 81, 90)
2. Determine `this` coercion behavior (line 163)
3. Create arguments objects (line 263)
4. Set function scope mode (line 423)

The `PropertyHandle.Delete()` path uses `context.CurrentScope.IsStrict` which reflects the environment strictness:
```csharp
// PropertyHandle.cs:136-140
var deleted = JsOps.DeletePropertyValueJsValue(_targetJsValue, new JsValue(_propertyName), _context);
if (!deleted && _isStrict)
{
    throw StandardLibrary.ThrowTypeError("Cannot delete property", _context, _context.RealmState);
}
```

#### ParseBlock Directive Prologue Issue
`ParseBlock()` at line 1604 calls `CheckForUseStrictDirective()` unconditionally for ALL blocks, not just function bodies and program-level code. Per ECMAScript spec (11.2.1), directive prologues only exist at:
1. The beginning of a Script or Module
2. The beginning of a FunctionBody, GeneratorBody, AsyncFunctionBody, or AsyncGeneratorBody

Regular blocks (try/catch, if/else, for loops) should NOT scan for directive prologues. A string literal `"use strict"` at the start of a block body is just an expression statement with no directive semantics.

**However**, this is mitigated by the fact that the block's `IsStrict` flag only affects parsing strictness (`EnterStrictContext`) and the block's own `BlockStatement.IsStrict` property. For function bodies, `function.Body.IsStrict` is what gets used at runtime, and the function body IS a valid directive prologue location.

#### Arrow Function Arguments Object (Confirmed Bug)

**In `SyncFunctionInvoker` (correct behavior):**
```csharp
// SyncFunctionInvoker.cs:164-165
_argumentsObjectNeeded =
    !IsArrowFunction && !argumentsIsParameterName && !canSkipArgumentsForBodyDeclaration;
```
Arrow functions correctly do NOT create their own `arguments` object.

**In `ExecutionPlanRunner.Environment.cs` (BUG):**
```csharp
// ExecutionPlanRunner.Environment.cs:261-264
var argumentsObject = _function.CreateArgumentsObject(_arguments, executionEnvironment, _realmState,
    _callable,
    _isStrict);
parameterEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
    isLexicalBinding: false);
```
This code creates an `arguments` object **unconditionally**, even for arrow functions. There is NO check for `_function.IsArrow`. This means arrow functions running through the IR execution path will have their own `arguments` binding that shadows the enclosing function's `arguments`.

This directly causes the `lexical-arguments.js` test to fail -- the arrow returns its own `arguments` object instead of the outer function's.

It also affects the `eval-code/direct/arrow-fn-body-cntns-arguments-*` tests -- eval inside an arrow should see the outer function's `arguments`, but sees the arrow's own.

## Root Cause Analysis

### Hypothesis 1 (High Confidence): Arrow Function Arguments Object in IR Path
**The IR `ExecutionPlanRunner.Environment.cs` creates an arguments object for arrow functions, which the `SyncFunctionInvoker` correctly skips.**

- **Evidence supporting:**
  - `SyncFunctionInvoker.cs:164-165` has explicit `!IsArrowFunction` guard.
  - `ExecutionPlanRunner.Environment.cs:261-264` has NO such guard.
  - This causes `lexical-arguments.js` and `arrow-fn-body-cntns-arguments-*` test failures.

- **Evidence against:**
  - This doesn't directly explain the `delete` or `implicit global` test failures.

### Hypothesis 2 (Medium Confidence): Strictness Incorrectly Propagated via Environment Chain
**The function environments are created with `_isStrict` which may be incorrectly `true` due to the scope chain or harness scripts.**

The chain is:
1. `_isStrict = function.Body.IsStrict || closure.IsStrict || isLexicallyStrict` (Core.cs:52)
2. `isLexicallyStrict = closureEnvironment.IsStrict` (FunctionExpressionExtensions.cs:410)
3. `IsStrict = _isStrictEffective = isStrict || (inheritStrictness && (enclosing?.IsStrict ?? false))` (JsEnvironment.cs:93)

If ANY environment in the scope chain has `IsStrict = true`, all child environments will inherit it. The `CompareArrayPatchScript` and other harness scripts are executed on the `GlobalEnvironment` -- if any of them inadvertently makes the global environment strict, ALL subsequent function environments would inherit strictness.

- **Evidence supporting:**
  - The `mapped-arguments-nonconfigurable-delete-*` tests throw TypeError (strict behavior) in sloppy mode.
  - The `delete` path uses `context.CurrentScope.IsStrict` (Helpers.cs:1283, 1296).
  - The `non-strict.js` arrow test throws ReferenceError (strict behavior) on implicit global creation.

- **Evidence against:**
  - No "use strict" found in any harness scripts or test262 helper code.
  - The `BaseRealmSnapshot` does not inject strictness.
  - Many other sloppy-mode tests PASS (112 arrow function tests pass vs 1 fail).

### Hypothesis 3 (Medium Confidence): ParseBlock Scans for Directives in Non-Function Blocks
**`ParseBlock()` at line 1604 calls `CheckForUseStrictDirective()` for ALL blocks, not just function bodies. This could cause `BlockStatement.IsStrict` to be `true` for non-function blocks that happen to start with a string literal.**

This matters because:
- For-in loops, try/catch blocks, etc. all call `ParseBlock()`
- If a harness function body starts with a string literal (not "use strict" specifically), this won't matter
- But if the `CompareArrayPatchScript` or harness files are parsed with `ParseBlock`, AND they happen to start with a string that resolves to "use strict", the containing block would be marked strict

- **Evidence supporting:**
  - `ParseBlock()` unconditionally calls `CheckForUseStrictDirective()` at line 1604.
  - ES spec says directive prologues only exist in function bodies and programs.

- **Evidence against:**
  - `CheckForUseStrictDirective()` only detects `"use strict"` specifically (checked at line 4374).
  - None of the harness scripts contain the literal "use strict".
  - The result of `CheckForUseStrictDirective` is OR-ed with `InStrictContext`, so non-function blocks in sloppy code would not become strict unless they contain the literal "use strict" directive.

## Recommended Fix

### Fix A: Arrow Function Arguments Object in ExecutionPlanRunner (High Priority)
In `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Environment.cs`, around line 261, add the same guard that `SyncFunctionInvoker` uses:

```csharp
// Before (current code, line 261-265):
var argumentsObject = _function.CreateArgumentsObject(_arguments, executionEnvironment, _realmState,
    _callable,
    _isStrict);
parameterEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
    isLexicalBinding: false);

// After (proposed fix):
if (!_function.IsArrow)
{
    // Need to replicate SyncFunctionInvoker's argumentsObjectNeeded logic:
    var parameterNames = /* collect from _function.Parameters */;
    var argumentsIsParam = parameterNames.Contains(Symbol.Arguments);
    var argumentsInBodyLex = bodyLexicalNames.Contains(Symbol.Arguments) && ...;
    var canSkipForBodyDecl = !hasParameterExpressions && argumentsInBodyLex;
    var argumentsObjectNeeded = !argumentsIsParam && !canSkipForBodyDecl;

    if (argumentsObjectNeeded)
    {
        var argumentsObject = _function.CreateArgumentsObject(_arguments, executionEnvironment, _realmState,
            _callable, _isStrict);
        parameterEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
            isLexicalBinding: false);
        if (!ReferenceEquals(parameterEnvironment, functionEnvironment))
        {
            functionEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                isLexicalBinding: false);
        }
    }
}
```

**Also** mirror the `SyncFunctionInvoker` logic at lines 225-232 for `new.target` -- arrow functions should NOT define their own `new.target` binding. The current code at `ExecutionPlanRunner.Environment.cs:224-232` already handles this correctly with `if (!isArrowFunction)`.

### Fix B: Investigate Strictness Source for Delete Failures (Medium Priority)
For the `mapped-arguments-nonconfigurable-delete-*` failures, the root cause needs further tracing. The recommended approach:

1. Add diagnostic logging to `PropertyHandle.Delete()` to log the actual `_isStrict` value and the environment chain.
2. Add a breakpoint or log in `ExecuteProgramDeleteComputedProperty` (Helpers.cs:1287-1298) to print `context.CurrentScope.IsStrict` and walk the scope chain.
3. Check if the `SyncFunctionInvoker` path also fails (these tests might only fail in the IR path).

If the strictness comes from the IR path's environment setup, the fix may be related to how `_isStrict` is computed at `ExecutionPlanRunner.Core.cs:52`. If `closure.IsStrict` is incorrectly `true`, the issue is in how the closure environment is established for the test function.

### Fix C: Restrict `CheckForUseStrictDirective` to Function Bodies (Low Priority)
In `ParseBlock()` at line 1604, only call `CheckForUseStrictDirective()` when the block is a function body:

```csharp
private BlockStatement ParseBlock(bool leftBraceConsumed = false)
{
    // ...
    var hasDirectiveStrict = leftBraceConsumed && CheckForUseStrictDirective();
    // leftBraceConsumed=true is ONLY passed from ParseFunctionTail and ParseArrowFunctionBody
    var isStrict = InStrictContext || hasDirectiveStrict;
    // ...
}
```

This is spec-compliant but lower priority since the current behavior doesn't appear to cause incorrect strictness (a block starting with `"use strict"` as an expression is unusual and harmless if no block starts with it).

## Test Plan
- [ ] Verify Fix A resolves `lexical-arguments.js` test
- [ ] Verify Fix A resolves `arrow-fn-body-cntns-arguments-*` tests
- [ ] Verify Fix B resolves `mapped-arguments-nonconfigurable-delete-*` tests (4 tests)
- [ ] Verify Fix resolves `non-strict.js` arrow function test
- [ ] Run related test suite: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 --filter "FullyQualifiedName~ArrowFunction"`
- [ ] Run mapped arguments suite: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 --filter "FullyQualifiedName~mapped"`
- [ ] Check for regressions: `dotnet test tests/Asynkron.JsEngine.Tests`
- [ ] Run full Test262 language suite to check overall impact

## Additional Notes

### Key Architectural Difference: SyncFunctionInvoker vs ExecutionPlanRunner
The codebase has two function execution paths:
1. **SyncFunctionInvoker** -- used for simple functions (fast path). Has correct arrow function handling.
2. **ExecutionPlanRunner** -- used for complex functions (generators, async, complex bodies). Missing arrow function guards.

When fixing the `ExecutionPlanRunner` path, ensure all arrow-specific behavior from `SyncFunctionInvoker` is replicated:
- `_argumentsObjectNeeded = !IsArrowFunction && ...` (SyncFunctionInvoker.cs:164)
- `_usesArguments = !IsArrowFunction && ...` (SyncFunctionInvoker.cs:117)
- Arrow `this` resolution (SyncFunctionInvoker.cs:167-175)
- Arrow `new.target` non-definition (already handled at ExecutionPlanRunner.Environment.cs:225)

### Mapped Arguments Delete Issue May Have a Different Root Cause
The `mapped-arguments-nonconfigurable-delete-*` tests fail for ALL 4 variants, suggesting a systematic issue rather than an edge case. Since these tests involve regular (non-arrow) functions, the arrow-specific fixes won't help. The issue may be:
1. The arguments object itself incorrectly reports being in strict mode
2. The function's execution environment inherits strictness from the test harness
3. The `PropertyHandle.Delete` path picks up strictness from an unexpected source

Further investigation with debug logging at `PropertyHandle.Delete()` is recommended.
