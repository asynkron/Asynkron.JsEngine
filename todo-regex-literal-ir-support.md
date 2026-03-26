# Investigation Report: ~128 Regex Literal Tests Fail Due to Missing IR Bytecode Support

## Problem Summary

All ~128 regex-related Test262 failures (across 3 categories) share a single root cause: the IR expression bytecode compiler (`ExpressionProgramCompiler.TryCompileExpression`) has no `case` arm for `RegexLiteralExpression`. Every test that uses a regex literal (`/pattern/flags`) in non-eval code fails with `NotSupportedException`.

## Affected Components

- **`src/Asynkron.JsEngine/Execution/Instructions/ExpressionProgramCompiler.cs`** (lines 95-187) -- missing `RegexLiteralExpression` case in `TryCompileExpression` switch
- **`src/Asynkron.JsEngine/Execution/Instructions/ExpressionOp.cs`** (line 6-73) -- missing `LoadRegexLiteral` enum value and corresponding op record
- **`src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Helpers.cs`** (line 195+) -- missing execution handler for the new op
- **`src/Asynkron.JsEngine/Ast/RegexLiteralExpression.cs`** -- the AST node (already exists, correctly parsed)
- **`src/Asynkron.JsEngine/Ast/Legacy/RegexLiteralExpressionExtensions.cs`** -- legacy AST evaluator (works correctly, used by eval path)
- **`src/Asynkron.JsEngine/StdLib/RegExp/RegExpHelper.cs`** line 15 -- `CreateRegExpLiteral` helper (already exists)

## Evidence Collected

### Test Output

All 128 tests produce the identical error class:

```
System.NotSupportedException: IR plan generation failed for script:
  BranchInstruction could not lower expression 'BinaryExpression' to bytecode
  [UnsupportedExpressionNode]: Expression bytecode does not yet support 'RegexLiteralExpression'.
```

Variants differ only in the parent instruction name:
- `BranchInstruction` (for tests using regex in conditionals like `if (/1/.source !== "1")`)
- `EvaluateAndDiscardInstruction` (for tests using regex as expression statements like `{ /1/; }`)
- `CallExpression` (for tests passing regex to functions like `assert.sameValue(...)`)

### Failure Breakdown

| Category | Unique .js files (x2 for strict/sloppy) | Test method |
|----------|------------------------------------------|-------------|
| `language/literals/regexp/` | ~31 files = 62 test runs | `Literals_regexp` |
| `annexB/language/literals/regexp/` | ~8 files = 16 test runs | `AnnexBTests.Language_literals_regexp` |
| `language/white-space/after-regular-expression-literal-*` | ~25 files = 50 test runs | `WhiteSpace` |
| `language/statementList/*-regexp-literal*` | ~8 files = 16 test runs | `StatementList` |
| `language/literals/regexp/named-groups/` | ~1 file = 2 test runs | `Literals_regexp_namedGroups` |
| `language/literals/regexp/early-err-modifiers-*` | ~1 file = 2 test runs | `Literals_regexp` |

**Total: ~148 test runs** (74 unique .js files x 2 strict/sloppy modes)

### Proof That Parsing Is Correct

The `eval-block-regexp-literal` and `eval-block-regexp-literal-flags` tests PASS. These use `eval('/1/;')` which routes through the legacy AST evaluator in `RegexLiteralExpressionExtensions.cs`, proving:
1. The lexer correctly parses regex literals
2. The parser correctly builds `RegexLiteralExpression` AST nodes
3. The `RegexLiteralExpression.Pattern` and `.Flags` properties are correct
4. `RegExpHelper.CreateRegExpLiteral()` correctly creates RegExp objects at runtime

The ONLY gap is in the IR bytecode path.

### Code Analysis

**The missing case (ExpressionProgramCompiler.cs:100-187):**

The `TryCompileExpression` switch handles `LiteralExpression`, `FunctionExpression`, `ClassExpression`, `IdentifierExpression`, etc., but has no arm for `RegexLiteralExpression`. It falls through to:

```csharp
default:
    failureReason = $"Expression bytecode does not yet support '{expression.GetType().Name}'.";
    return false;
```

**The existing legacy path (RegexLiteralExpressionExtensions.cs:17-20):**

```csharp
private static JsValue EvaluateRegexLiteral(this RegexLiteralExpression regex, EvaluationContext context)
{
    return new JsValue(CreateRegExpLiteral(regex.Pattern, regex.Flags, context.RealmState));
}
```

This shows exactly what the IR op needs to do: call `RegExpHelper.CreateRegExpLiteral(pattern, flags, realmState)`.

## Root Cause Analysis

### Hypothesis 1 (Confirmed): Missing IR Expression Op for RegexLiteralExpression

**Confidence: HIGH -- this is definitively the root cause.**

The IR bytecode compiler has a switch statement that maps AST expression nodes to bytecode operations. `RegexLiteralExpression` was never added to this switch. When the IR plan builder encounters a regex literal, it fails to compile the expression program, causing `EvaluateProgramJsValueCore` (line 559) to throw `NotSupportedException`.

- Evidence supporting: 100% of failures show the same `UnsupportedExpressionNode` error message naming `RegexLiteralExpression`
- Evidence against: None. This is not a hypothesis -- it is directly confirmed by the error message and source code.

## Recommended Fix

### Option A: Add RegexLiteralExpression Support to IR Bytecode (Recommended)

Three files need changes:

**Step 1: Add enum value to `ExpressionOp.cs`** (around line 10):

```csharp
internal enum ExpressionOpKind : byte
{
    LoadLiteral,
    LoadRegexLiteral,    // <-- ADD THIS
    LoadFunctionLiteral,
    LoadClassLiteral,
    // ...
```

**Step 2: Add op record to `ExpressionOp.cs`** (after line 109):

```csharp
internal sealed record LoadRegexLiteralExpressionOp(string Pattern, string Flags)
    : ExpressionOp(ExpressionOpKind.LoadRegexLiteral);
```

**Step 3: Add case to `ExpressionProgramCompiler.cs` TryCompileExpression** (after line 105):

```csharp
case RegexLiteralExpression regex:
    builder.Add(new LoadRegexLiteralExpressionOp(regex.Pattern, regex.Flags));
    failureReason = null;
    return true;
```

**Step 4: Add execution handler to `TypedAstEvaluator.ExecutionPlanRunner.Helpers.cs`** (after the LoadLiteralExpressionOp case around line 201):

```csharp
case LoadRegexLiteralExpressionOp loadRegex:
    stack[stackIndex++] = new JsValue(
        RegExpHelper.CreateRegExpLiteral(
            loadRegex.Pattern, loadRegex.Flags, context.RealmState));
    stackFlags[stackIndex - 1] = false;
    programCounter++;
    break;
```

**Step 5: Update `ExecutionPlanPrinter.cs`** if it has a formatter for expression ops (to display regex ops in diagnostics/debug output).

- Pros: Clean, minimal change following existing patterns exactly (mirrors LoadLiteralExpressionOp). Fixes all ~148 test runs at once.
- Cons: None. This is the natural extension point.

## Test Plan

- [ ] Verify fix resolves all `language/literals/regexp/` tests: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 --filter "FullyQualifiedName~Literals_regexp" -c Release`
- [ ] Verify fix resolves all `language/white-space/after-regular-expression-literal-*` tests: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 --filter "FullyQualifiedName~WhiteSpace" -c Release`
- [ ] Verify fix resolves all `language/statementList/*-regexp-literal*` tests: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 --filter "FullyQualifiedName~StatementList" -c Release`
- [ ] Verify fix resolves AnnexB regex tests: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 --filter "FullyQualifiedName~AnnexBTests.Language_literals_regexp" -c Release`
- [ ] Run full internal test suite for regressions: `dotnet test tests/Asynkron.JsEngine.Tests -c Release`
- [ ] Verify `eval-block-regexp-literal` tests still pass (regression check on legacy path)
- [ ] Check for any additional regex tests that might become unblocked

## Additional Notes

- The parser and lexer regex disambiguation logic (`IsRegexContext()`, `WasLastBraceAStatementBlock()`, brace kind tracking) is well-implemented and not involved in these failures.
- The `ReadRegexLiteral()` lexer method correctly handles character classes, escape sequences, and flags.
- Some of the `language/literals/regexp/` tests may still fail AFTER adding IR support if they test regex semantics (named groups, unicode escapes, sticky flag behavior, etc.) that the `RegExpHelper.CreateRegExpLiteral` runtime implementation does not fully handle. The IR fix will surface those as runtime failures rather than compilation failures.
- The `early-err-modifiers-other-code-point-combining-s.js` test likely tests regex modifier syntax (`(?ims:...)`) which may need separate parser/runtime support beyond this IR fix.
