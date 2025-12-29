# Failing Test262 Tests - Analysis and Fix Plans

## Summary

| Category | Tests | Priority | Root Cause |
|----------|-------|----------|------------|
| Catch parameter shadowing | 2 | High | `blocksFunctionScopeOverride: true` on catch params |
| Arrow function super() | 6 | High | `LexicalThisEnvironment` not propagated correctly |
| TDZ/Const fast path | 3 | High | Slot-only write path bypasses checks |
| Iterator error handling | 4 | Medium | Value getter errors trigger incorrect iterator close |
| yield* in try/catch | 2 | Medium | `_tryStack` not preserved across yield points |
| Finally completion values | 1 | Medium | Finally throw sets context flag, doesn't throw signal |

---

## 1. Block Scope Shadowing (2 tests)

**Failing Tests:**
- `language/block-scope/shadowing/catch-parameter-shadowing-var-variable.js`
- `language/block-scope/shadowing/parameter-name-shadowing-catch-parameter.js`

### Problem

```javascript
function fn() {
  var a = 1;
  try { throw 'stuff3'; }
  catch (a) {
    assert.sameValue(a, 'stuff3');  // OK
  }
  assert.sameValue(a, 1);  // FAILS: gets 'stuff3' instead of 1
}
```

### Root Cause

Catch parameters are defined with `blocksFunctionScopeOverride: true` because `BindingMode.DefineLet` is used. Per ECMAScript, catch parameters should NOT block var declarations from reaching the function scope.

**Code path:**
1. `TryStatementExtensions.cs:57` → `DefineBindingTarget(thrownValue, catchEnv, context, false)`
2. Uses `BindingMode.DefineLet`
3. `IdentifierBindingExtensions.cs:39-41` → `DefineJsValue(..., blocksFunctionScopeOverride: true)`

### Fix Plan

In `JsEnvironment.cs` → `TryAssignBlockedBindingJsValue`, skip catch parameters:

```csharp
if (current.IsSimpleCatchParameter(name))
{
    current = current.Enclosing;
    continue;  // Skip catch parameters - var should go to function scope
}
```

**Files:** `JsEnvironment.cs`

---

## 2. Arrow Functions, Super Calls, and Class TDZ (6 tests)

**Failing Tests:**
- `language/expressions/arrow-function/lexical-supercall-from-immediately-invoked-arrow.js`
- `language/expressions/class/constructor-this-tdz-during-initializers.js`
- `language/statements/class/subclass/class-definition-null-proto-this.js`
- `language/statements/class/subclass/derived-class-return-override-catch-finally-arrow.js`
- `language/statements/class/subclass/derived-class-return-override-finally-super-arrow.js`
- `language/statements/class/subclass/derived-class-return-override-for-of-arrow.js`

### Problem

Arrow functions inside derived class constructors can call `super()`, which should initialize `this` in the constructor's environment. Several edge cases fail:

1. **Immediately invoked arrow with super()** - `super()` not updating outer `this`
2. **extends null** - Class should be derived but has no callable super
3. **super() in finally after return** - `IsThisInitialized` incorrectly restored to false
4. **super() via iterator return()** - Environment chain broken

### Root Causes

| Issue | Description |
|-------|-------------|
| `LexicalThisEnvironment` propagation | Arrow functions may not correctly capture constructor's environment |
| `extends null` handling | Class extending `null` not marked as derived |
| Finally state restoration | `IsThisInitialized` restored to `false` after finally runs `super()` |
| Iterator close + super | `super()` from iterator `return()` doesn't update `this` correctly |

### Fix Plan

1. **Fix `LexicalThisEnvironment` capture** - Walk environment chain to find the environment that owns `Symbol.This`, store THAT in `_lexicalThisEnvironment`

2. **Fix `extends null`** - Mark class as derived (`_isDerivedClassConstructor = true`) even when extending null, but prevent `super()` call

3. **Fix finally state** - Don't restore `IsThisInitialized=false` when `super()` was called during finally

4. **Fix iterator close** - Ensure `super()` from iterator `return()` correctly propagates `this` initialization

**Files:** `SyncFunctionInvoker.cs`, `ExpressionNodeExtensions.cs`, `TryStatementExtensions.cs`, `IteratorDriverPlanExtensions.cs`

---

## 3. For-Of Loop Issues (10 tests)

**Failing Tests:**
- `language/statements/for-of/head-const-bound-names-fordecl-tdz.js`
- `language/statements/for-of/head-let-bound-names-fordecl-tdz.js`
- `language/statements/for-of/head-using-bound-names-fordecl-tdz.js`
- `language/statements/for-of/iterator-close-throw-get-method-abrupt.js`
- `language/statements/for-of/iterator-close-throw-get-method-non-callable.js`
- `language/statements/for-of/iterator-next-error.js`
- `language/statements/for-of/iterator-next-result-value-attr-error.js`
- `language/statements/for-of/return-from-finally.js`
- `language/statements/for-of/yield-star-from-catch.js`
- `language/statements/for-of/yield-star-from-try.js`

### Problems by Category

#### 3a. TDZ in for-of head (3 tests)

```javascript
let x = 1;
for (const x of [x]) {}  // Should throw ReferenceError - inner x is in TDZ
```

**Root Cause:** Slot fast path may bypass TDZ check when evaluating iterable.

**Fix:** Add TDZ check before iterable evaluation when using let/const.

**Files:** `ForEachStatementExtensions.cs`

#### 3b. Iterator Protocol Errors (4 tests)

| Test | Expected Behavior |
|------|-------------------|
| `iterator-next-error` | When `next()` throws, `return()` should NOT be called |
| `iterator-next-result-value-attr-error` | When `value` getter throws, `return()` should NOT be called |
| `iterator-close-throw-get-method-abrupt` | When loop throws and getting `return` throws, original error propagates |
| `iterator-close-throw-get-method-non-callable` | When `return` is not callable, original error propagates |

**Root Cause:**
- `value` getter errors at line ~260 in `IteratorDriverPlanExtensions.cs` not handled before iterator close
- Error suppression logic for non-callable `return` may not apply correctly

**Fix:**
1. Wrap `value` getter access in try/catch, propagate error without iterator close
2. Ensure `preserveExistingThrow` properly suppresses TypeError from non-callable return

**Files:** `IteratorDriverPlanExtensions.cs`, `JsObjectExtensions.cs`

#### 3c. Control Flow (1 test)

```javascript
function* values() { yield 1; throw new Error('unreachable'); }
var result = (function() {
  for (var x of values()) {
    try {} finally { return 34; }  // Should exit immediately
  }
})();
// result should be 34, iterator should be closed
```

**Root Cause:** `return` in finally within for-of needs proper iterator close sequencing.

**Files:** `ExecutionPlanRunner.cs`

#### 3d. yield* in try/catch (2 tests)

```javascript
function*() {
  for (var x of dataIterator) {
    try {
      yield * values();  // Must preserve try context across yield
    } catch (err) {}
  }
}
```

**Root Cause:** `_tryStack` not preserved across yield points.

**Fix:** Serialize/restore try stack state before/after yielding.

**Files:** `ExecutionPlanRunner.cs`

---

## 4. Try/Catch Completion Values and TDZ (3 tests)

**Failing Tests:**
- `language/statements/try/completion-values-fn-finally-abrupt.js`
- `language/statements/let/function-local-closure-set-before-initialization.js`
- `language/statements/const/syntax/const-invalid-assignment-next-expression-for.js`

### 4a. Finally Completion Values

```javascript
function fn() {
  try { throw 'try'; }
  catch { throw 'catch'; }
  finally { throw 'finally'; }  // This should be the thrown error
}
```

**Root Cause:** Finally throw may not propagate as exception - only sets `context.IsThrow` flag without throwing `ThrowSignal`.

**Fix:** Convert context-based throws to `ThrowSignal` exceptions when exiting finally.

**Files:** `TryStatementExtensions.cs`

### 4b. Let TDZ Writes

```javascript
(function() {
  function f() { x = 1; }  // Captures x
  f();  // Should throw ReferenceError - x is in TDZ
  let x;
})();
```

**Root Cause:** Slot-only fast path in `TryWriteIdentifierWithSlot` (line ~1247) writes directly without checking TDZ:

```csharp
slots[slotIndex] = value;  // No TDZ check!
```

**Fix:** Check if slot value is `JsValue.Uninitialized` before writing in fast path.

**Files:** `JsEnvironment.cs`

### 4c. Const Assignment in For-Loop

```javascript
for (const i = 0; i < 1; i++) {}  // Should throw TypeError
```

**Root Cause:** Slot-only fast path bypasses const reassignment check.

**Fix:** Store const/lexical flags alongside slots, or check `IsConstBinding` before slot writes.

**Files:** `JsEnvironment.cs`, `UnaryExpressionExtensions.cs`

---

## Implementation Priority

### Phase 1: High Priority (11 tests)

1. [ ] **Catch parameter shadowing** - Skip catch params in `TryAssignBlockedBindingJsValue`
2. [ ] **TDZ/Const fast path** - Add checks to slot-only write path
3. [ ] **Arrow super() fixes** - Fix `LexicalThisEnvironment` capture and propagation

### Phase 2: Medium Priority (9 tests)

4. [ ] **Iterator error handling** - Handle value getter exceptions, fix error suppression
5. [ ] **Finally completion values** - Convert context throws to `ThrowSignal`
6. [ ] **yield* in try/catch** - Preserve `_tryStack` across yield points

### Phase 3: Edge Cases (remaining)

7. [ ] **extends null** - Mark as derived, prevent super() call
8. [ ] **Finally + super()** - Don't restore `IsThisInitialized=false` after finally
9. [ ] **return-from-finally in for-of** - Proper iterator close sequencing

---

## Files Summary

| File | Issues |
|------|--------|
| `JsEnvironment.cs` | Catch param shadowing, TDZ writes, const checks |
| `TryStatementExtensions.cs` | Finally completion, `IsThisInitialized` restoration |
| `SyncFunctionInvoker.cs` | `LexicalThisEnvironment` capture |
| `IteratorDriverPlanExtensions.cs` | Value getter errors, iterator close |
| `ExecutionPlanRunner.cs` | `_tryStack` preservation, return-from-finally |
| `ForEachStatementExtensions.cs` | TDZ in for-of head |
| `ExpressionNodeExtensions.cs` | extends null handling |
| `JsObjectExtensions.cs` | Iterator close error suppression |
| `UnaryExpressionExtensions.cs` | Const increment check |
