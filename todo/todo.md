# Test262 Failure Analysis

## Summary

**Total Failing Tests: ~250**

| Root Cause | Tests | Priority | Status |
|-----------|-------|----------|--------|
| Completion value semantics | 85 | High | In Progress |
| For-of iterator protocol | 32 | High | Not Started |
| Function name inference | 30 | Medium | Not Started |
| Module system (not implemented) | 25 | High | Not Started |
| Try/catch destructuring errors | 24 | High | Not Started |
| Lexical scope in loops | 21 | High | Not Started |
| RegExp multi-line comment parsing | 8 | Medium | Not Started |
| HTML-like comment delimiters | 4 | Low | Not Started |
| Prefix increment/decrement | 4 | Low | Not Started |
| Class definition basics | 2 | Medium | Not Started |
| Class name in static initializers | 2 | Low | Not Started |
| Generator implicit naming | 2 | Low | Not Started |
| Class strict mode enforcement | 1 | Low | Not Started |
| Eval var/delete edge case | 1 | Low | Not Started |

---

## 1. Completion Value Semantics (85 tests) - HIGH PRIORITY

**Affected Tests:**
- `cptn-*` tests in: for, for-of, if, switch, try, eval-code
- `S12.14_A9-A11` try tests

**Problem:**
Statements/expressions not returning correct completion values per ECMAScript spec. The spec defines completion values for all statements (Normal, Break, Continue, Return, Throw).

**Files to Modify:**
- `StatementNodeExtensions.cs`
- `IfStatementExtensions.cs`
- `ForStatementExtensions.cs`
- `SwitchStatementExtensions.cs`
- `TryStatementExtensions.cs`
- `TypedAstEvaluator.cs`

### Test Bomb Results (2024-12-30)

Systematic testing revealed **two distinct bugs**:

#### Bug 1: Previous Statement Completion Value Leaks Through Empty Blocks

When there's a statement before a compound statement (try/catch, if/else), and the executed block is **empty**, the previous statement's completion value incorrectly propagates:

| Test | Code | Expected | Actual |
|------|------|----------|--------|
| H5 | `eval('1; try { throw null; } catch (e) { }')` | undefined | **1** |
| H6 | `eval('1; try { } catch (e) { }')` | undefined | **1** |
| H13 | `eval('5; if (false) { 1; } else { }')` | undefined | **5** |
| H15 | `eval('1; for (var i = 0; i < 1; i++) { }')` | undefined | **0** |
| H17 | `eval('1; switch (1) { case 1: break; }')` | undefined | **True** |

**Fix approach:**
- When emitting an empty block, emit an instruction that sets completion value to `undefined`
- Or ensure the completion value tracking resets at statement boundaries

#### Bug 2: Finally Normal Completion Incorrectly Overwrites Try Value

Per ES spec 13.15.8, a finally block with **normal completion** should preserve the try/catch completion value:

| Test | Code | Expected | Actual |
|------|------|----------|--------|
| H9 | `eval('try { 1; } finally { 9; }')` | 1 | **9** |

The spec says: "If F.[[Type]] is normal, set F to C" - meaning finally's normal completion should preserve the previous value.

**Fix approach:**
1. Track the try/catch completion value before entering finally
2. If finally completes normally, restore the try/catch completion value
3. Only use finally's completion if it's abrupt (return/throw/break/continue)

#### Bug 3: Break From Finally Causes Infinite Loop

The `Replicate_BreakFromFinally` test shows an infinite loop where BREAK instruction at [10] keeps going to [7] repeatedly without exiting the loop.

**Fix approach:**
- Investigate `TryEmitter` and how break targets are calculated within finally blocks
- Ensure break instruction properly pops all try frames and exits to the correct target

---

## 2. For-of Iterator Protocol (32 tests) - HIGH PRIORITY

**Affected Tests:**
- `language/statements/for-of/break-from-finally.js`
- `language/statements/for-of/continue-from-catch.js`
- `language/statements/for-of/continue-from-finally.js`
- `language/statements/for-of/generator-close-via-break.js`
- `language/statements/for-of/generator-close-via-continue.js`
- `language/statements/for-of/generator-close-via-throw.js`
- `language/statements/for-of/iterator-close-via-break.js`
- `language/statements/for-of/iterator-close-via-continue.js`
- `language/statements/for-of/iterator-close-via-throw.js`
- `language/statements/for-of/map*.js` (Map iteration during modification)
- `language/statements/for-of/set*.js` (Set iteration during modification)
- `language/statements/for-of/yield-star-from-catch.js`
- `language/statements/for-of/yield-star-from-try.js`

**Problem:**
For-of loops with iterators (generators, Maps, Sets, proxies) failing. Includes:
- Iterator close semantics (IteratorClose, `.return()` method)
- Generator integration
- Map/Set expansion/contraction during iteration
- Proxy as iterator

**Files to Modify:**
- `ForOfStatementExtensions.cs`
- `JsIterator.cs`
- `IteratorDriverPlanExtensions.cs`
- `GeneratorIR/GeneratorInterpreter.cs`

**Fix approach:**
1. Implement full iterator protocol with IteratorClose
2. Call `.return()` method on break/continue/throw
3. Handle generator-specific close behavior

---

## 3. Function Name Inference (30 tests) - MEDIUM PRIORITY

**Affected Tests:**
- `language/statements/const/fn-name-*.js` (10 tests)
- `language/statements/let/fn-name-*.js` (10 tests)
- `language/statements/variable/fn-name-*.js` (10 tests)

**Problem:**
Named function expressions/classes not setting `.name` property correctly. When assigning arrow/class/function/generator to a variable, spec requires inferring the name.

**Files to Modify:**
- `VariableDeclarationExtensions.cs`

**Fix approach:**
- When assigning function expression to variable, call `SetFunctionName(func, variableName)`

---

## 4. Module System (25 tests) - HIGH PRIORITY (Major Feature)

**Affected Tests:**
- All `language/module-code/*` tests

**Problem:**
ES6 modules are not supported yet. Tests for:
- `import`/`export` statements
- Module instantiation (`instn-*`)
- Module eval (`eval-export-dflt-*`)
- Top-level await

**Files to Create:**
- `ModuleParser.cs`
- `ModuleEvaluator.cs`
- Import/export AST nodes

**Note:** This is a major missing feature requiring new parsing/evaluation infrastructure. Separate project phase.

---

## 5. Try/Catch Destructuring Errors (24 tests) - HIGH PRIORITY

**Affected Tests:**
- All `language/statements/try/dstr/*` tests

**Problem:**
Destructuring in catch clauses not handling errors correctly. When destructuring patterns in `catch (e)` fail (iterator errors, type errors, unresolvable bindings), the engine should throw/propagate the error properly.

**Files to Modify:**
- `TryStatementExtensions.cs`
- `DestructuringPatternExtensions.cs`

**Fix approach:**
- Ensure destructuring errors in catch parameters throw correctly instead of being swallowed

---

## 6. Lexical Scope in Loops (21 tests) - HIGH PRIORITY

**Affected Tests:**
- `language/statements/for/scope-*.js`
- `language/statements/for-of/scope-*.js`
- `language/statements/switch/scope-lex-*.js`
- `language/statements/let/syntax/let-outer-inner-let-bindings.js`
- `language/statements/const/syntax/const-outer-inner-let-bindings.js`

**Problem:**
Lexical scoping not isolated correctly per iteration/case. Tests verify that `let`/`const` in loop heads create per-iteration bindings.

**Files to Modify:**
- `LoopPlanExtensions.cs`
- `ScopeDynamicnessAnalyzer.cs`
- `ForStatementExtensions.cs`

---

## 7. RegExp Multi-line Comment Parsing (8 tests) - MEDIUM PRIORITY

**Affected Tests:**
- `language/literals/regexp/S7.8.5_A1.1_T2.js`
- `language/literals/regexp/S7.8.5_A1.4_T2.js`
- `language/literals/regexp/S7.8.5_A2.1_T2.js`
- `language/literals/regexp/S7.8.5_A2.4_T2.js`

**Problem:**
RegExp literals with `/* */` comments not parsed correctly. Multi-line comments inside regexp literals may terminate the regexp prematurely.

**Files to Modify:**
- `Lexer.cs`

---

## 8. HTML-like Comment Delimiters (4 tests) - LOW PRIORITY (Quick Win)

**Affected Tests:**
- `language/comments/S7.4_A5.js`
- `language/comments/S7.4_A6.js`

**Problem:**
HTML-style comments (`<!--`, `-->`) not supported. ECMAScript requires supporting these legacy comment forms for web compatibility (Annex B).

**Files to Modify:**
- `Lexer.cs`

**Fix approach:**
- Add support for `<!--` (treat as line comment)
- Add support for `-->` (treat as line comment if at start of line)

---

## 9. Prefix Increment/Decrement (4 tests) - LOW PRIORITY (Quick Win)

**Affected Tests:**
- `language/expressions/prefix-increment/S11.4.4_A2.2_T1.js`
- `language/expressions/prefix-decrement/S11.4.5_A2.2_T1.js`

**Problem:**
Prefix `++`/`--` operators returning wrong completion value.

**Files to Modify:**
- `UnaryExpressionExtensions.cs`

---

## 10. Class Definition Basics (2 tests) - MEDIUM PRIORITY

**Affected Tests:**
- `language/statements/class/definition/basics.js`

**Problem:**
Core class functionality broken - likely constructor/prototype setup or method definition.

**Files to Modify:**
- `ClassDeclarationExtensions.cs`
- `ClassExpressionExtensions.cs`

---

## 11-14. Edge Cases (8 tests total) - LOW PRIORITY

| Issue | Tests | File |
|-------|-------|------|
| Class name in static initializers | 2 | ClassExpressionExtensions.cs |
| Generator implicit naming | 2 | GeneratorExpressionExtensions.cs |
| Class strict mode enforcement | 1 | Class evaluation context |
| Eval var/delete edge case | 1 | EvalExtensions.cs |

---

## Recommended Fix Order

### Round 1: Foundational Issues (High Impact)
1. **Completion value semantics** → fixes 85 tests
2. **Try/catch destructuring errors** → fixes 24 tests
3. **For-of iterator protocol** → fixes 32 tests

### Round 2: Medium Impact
4. **Function name inference** → fixes 30 tests
5. **Lexical scope in loops** → fixes 21 tests
6. **Class definition basics** → fixes 2 tests

### Round 3: Quick Wins
7. **HTML comments** (4 tests) - ~30 lines of code
8. **Prefix inc/dec** (4 tests) - simple return value fix
9. **RegExp parsing** (8 tests) - lexer fix

### Round 4: Major Feature
10. **Module system** (25 tests) - separate project phase

---

## Test Verification Commands

```bash
# Run specific category
dotnet test tests/Asynkron.JsEngine.Tests.Test262 --filter "FullyQualifiedName~Statements_for"

# Run completion value test bomb
dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~CatchBlockTestBomb"

# Run all Test262 tests
dotnet test tests/Asynkron.JsEngine.Tests.Test262
```

---

## IR Catch Block Implementation (Previous Session)

### Original Root Cause

The catch block delegation to AST evaluation causes thrown values to be lost when:
1. `assert.throws` runs via IR (no eval in its body)
2. Its try block calls a function that uses AST path (due to `eval`)
3. Error propagates from AST back to IR catch handler
4. The synthetic `let thrown = #catchSlot` reads `undefined` instead of the thrown value

The fix: Emit catch blocks entirely in IR, avoiding AST delegation.

### Implementation Plan

#### 1. Add `EnterCatchInstruction` (Instructions.cs)
```csharp
internal sealed record EnterCatchInstruction(
    int Next,
    Symbol? CatchParameterSymbol,
    int ScopeId,
    int SlotCount,
    ImmutableDictionary<Symbol, int> SlotMap)
    : ExecutionInstruction(InstructionKind.EnterCatch, Next);
```

#### 2. Files to Modify
1. `src/Asynkron.JsEngine/Execution/Instructions/Instructions.cs` - Add EnterCatchInstruction
2. `src/Asynkron.JsEngine/Execution/Instructions/InstructionKind.cs` - Add EnterCatch enum
3. `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Types.cs` - Add ThrownValue to TryFrame
4. `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Completion.cs` - Store thrown value in frame
5. `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.cs` - Add EnterCatch handler
6. `src/Asynkron.JsEngine/Execution/Emitters/TryEmitter.cs` - Emit catch as IR
7. `src/Asynkron.JsEngine/Execution/ExecutionPlanBuilder.cs` - Remove BuildCatchBlock

### Test Files Created
- `tests/Asynkron.JsEngine.Tests/CatchCompletionValueReplicationTest.cs`
- `tests/Asynkron.JsEngine.Tests/CatchBlockTestBomb.cs` - 27 hypothesis tests
