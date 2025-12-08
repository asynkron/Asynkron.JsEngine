# Fix Import Attributes for JSON Modules

## Problem Summary

The engine doesn't support import attributes syntax (`with { type: 'json' }`), causing JSON module imports to fail with a parse error. The parser expects a semicolon immediately after the module path, but import attributes syntax places `with { ... }` before the semicolon.

## Hard Facts

- Import attributes are a Stage 4 ECMAScript proposal (shipped in Chrome 123+, Firefox 123+, Safari 17.4+)
- Syntax: `import data from './file.json' with { type: 'json' };`
- The `type: 'json'` attribute tells the engine to parse the module as JSON
- JSON modules export their parsed value as the default export
- The `with` keyword is a contextual keyword in import statements
- Export statements also support import attributes: `export { x } from './module.js' with { type: 'json' };`

## Test Status

**Failing Tests** (10 tests):
- `language/import/import-attributes/json-extensibility-array.js`
- `language/import/import-attributes/json-extensibility-object.js`
- `language/import/import-attributes/json-idempotency.js`
- `language/import/import-attributes/json-value-array.js`
- `language/import/import-attributes/json-value-boolean.js`
- `language/import/import-attributes/json-value-null.js`
- `language/import/import-attributes/json-value-number.js`
- `language/import/import-attributes/json-value-object.js`
- `language/import/import-attributes/json-value-string.js`
- `language/import/import-attributes/json-via-namespace.js`

## What We Have

### Current Implementation in TypedAstParser.cs

The `ParseImportStatement()` method (lines 1112-1179) parses import statements but doesn't handle import attributes:

```csharp
private StatementNode ParseImportStatement()
{
    var keyword = Previous();

    // ... parse bindings ...

    ConsumeContextualKeyword("from", "Expected 'from' in import statement.");
    var moduleTokenFinal = Consume(TokenType.String, "Expected module path.");
    var modulePathFinal = GetStringLiteralValue(moduleTokenFinal);
    Consume(TokenType.Semicolon, "Expected ';' after import statement.");  // <-- Problem: no check for 'with'
    return new ImportStatement(CreateSourceReference(keyword), modulePathFinal, defaultBinding,
        namespaceBinding, namedImports, isDeferred);
}
```

### Current ImportStatement AST Node (Statements.cs line 294)

```csharp
public sealed record ImportStatement(
    SourceReference? Source,
    string ModulePath,
    Symbol? DefaultBinding,
    Symbol? NamespaceBinding,
    ImmutableArray<ImportBinding> NamedImports,
    bool IsDeferred) : ModuleStatement(Source);
```

The AST node doesn't have a property for attributes.

## Why It's Failing

1. **Parser doesn't recognize `with` keyword**: After parsing the module path, the parser immediately expects a semicolon
2. **AST node lacks Attributes property**: `ImportStatement` can't store import attributes
3. **Module loader doesn't check attributes**: The engine needs to recognize `type: 'json'` and parse accordingly

## Solution Path

### Step 1: Add ImportAttribute AST Node

Create a new AST node to represent import attributes:

```csharp
public sealed record ImportAttribute(
    SourceReference? Source,
    string Key,    // e.g., "type"
    string Value   // e.g., "json"
) : AstNode(Source);
```

### Step 2: Update ImportStatement AST Node

Add an `Attributes` property to `ImportStatement`:

```csharp
public sealed record ImportStatement(
    SourceReference? Source,
    string ModulePath,
    Symbol? DefaultBinding,
    Symbol? NamespaceBinding,
    ImmutableArray<ImportBinding> NamedImports,
    bool IsDeferred,
    ImmutableArray<ImportAttribute> Attributes) : ModuleStatement(Source);  // <-- Add Attributes
```

### Step 3: Update Parser to Handle `with` Keyword

Modify `ParseImportStatement()` to check for `with` before consuming the semicolon:

```csharp
// After parsing module path
var attributes = ImmutableArray<ImportAttribute>.Empty;
if (CheckContextualKeyword("with"))
{
    Advance(); // consume 'with'
    attributes = ParseImportAttributes();
}
Consume(TokenType.Semicolon, "Expected ';' after import statement.");
```

Add `ParseImportAttributes()` method:

```csharp
private ImmutableArray<ImportAttribute> ParseImportAttributes()
{
    Consume(TokenType.LeftBrace, "Expected '{' after 'with'.");
    var builder = ImmutableArray.CreateBuilder<ImportAttribute>();

    if (!Check(TokenType.RightBrace))
    {
        do
        {
            // Key can be identifier or string literal
            string key;
            if (Check(TokenType.String))
            {
                key = GetStringLiteralValue(Advance());
            }
            else
            {
                key = Consume(TokenType.Identifier, "Expected attribute key.").Lexeme;
            }

            Consume(TokenType.Colon, "Expected ':' after attribute key.");

            var valueToken = Consume(TokenType.String, "Expected string literal for attribute value.");
            var value = GetStringLiteralValue(valueToken);

            builder.Add(new ImportAttribute(CreateSourceReference(valueToken), key, value));
        }
        while (Match(TokenType.Comma) && !Check(TokenType.RightBrace));
    }

    Consume(TokenType.RightBrace, "Expected '}' after import attributes.");
    return builder.ToImmutable();
}
```

### Step 4: Update Module Loader

Modify `EvaluateModule()` in `JsEngine.cs` (or module loading code) to:

1. Check for `type: 'json'` attribute on imports
2. If present, parse the module content as JSON instead of JavaScript
3. Create a synthetic module with the JSON value as the default export

### Step 5: Handle Export Statements with Attributes

Export statements (`export ... from '...' with { ... }`) also need attribute support. Update `ParseExportStatement()` similarly.

## Key Files

- `src/Asynkron.JsEngine/Ast/Statements.cs` - `ImportStatement` AST node definition
- `src/Asynkron.JsEngine/Parser/TypedAstParser.cs` - `ParseImportStatement()` method
- `src/Asynkron.JsEngine/JsEngine.cs` - Module loading and evaluation

## Implementation Steps

1. Add `ImportAttribute` record to `Statements.cs`
2. Update `ImportStatement` record to include `Attributes` property
3. Add `ParseImportAttributes()` method to `TypedAstParser.cs`
4. Update `ParseImportStatement()` to call `ParseImportAttributes()` when `with` is present
5. Update all `ImportStatement` instantiation sites to include empty attributes array
6. Modify module loader to recognize JSON modules based on `type: 'json'` attribute
7. Implement JSON module parsing (call `JSON.parse` and wrap result as default export)
8. Update export statement parsing for re-exports with attributes
9. Test with the failing Test262 tests

## ECMAScript Specification References

- [Import Attributes Proposal](https://github.com/tc39/proposal-import-attributes)
- [JSON Modules Proposal](https://github.com/tc39/proposal-json-modules)

## Fix Applied (December 8, 2025)

**7 out of 10 JSON module tests now pass!**

### Changes Made

1. **`src/Asynkron.JsEngine/Ast/Statements.cs`**:
   - Added `ImportAttribute` record at line 311
   - Updated `ImportStatement` to include optional `Attributes` property with default value

2. **`src/Asynkron.JsEngine/Parser/TypedAstParser.cs`**:
   - Added `ParseOptionalImportAttributes()` method that checks for `TokenType.With` (not contextual keyword, since `with` is a JavaScript keyword)
   - Added `ParseImportAttributes()` method to parse `{ key: 'value' }` syntax
   - Updated `ParseImportStatement()` to call these methods before consuming semicolon

3. **`src/Asynkron.JsEngine/JsEngine.cs`**:
   - Added `IsJsonModule()` helper to detect `type: 'json'` attribute
   - Added `CreateJsonModule()` method that:
     - Parses JSON using `StandardLibrary.ParseJsonWithReviver`
     - Creates a synthetic module with the JSON value as default export
     - Sets up both the exports object AND the module environment binding for "default"
   - Updated `LoadModule()` and `LoadModuleForInstantiation()` to pass attributes through

### Key Insight

The critical fix was recognizing that:
1. `with` is a **keyword** (`TokenType.With`), not a contextual keyword - so `CheckContextualKeyword("with")` would never match
2. JSON modules need to define the "default" binding in **both** the exports object AND the module environment for import binding resolution to work

### Remaining Test Failures

3 tests still fail due to a **separate bug in JSON.parse** where `null` values in arrays/objects are not preserved correctly:
- `json-value-array.js` - fails because `null` in the array becomes `[object Object]`
- `json-value-object.js` - fails because `null` property value becomes an object
- `json-extensibility-array.js` - depends on json-value-array fixture

These failures are **not related to import attributes** - they're a JSON parsing bug that should be fixed separately.

### Tests Now Passing

✅ `json-value-string.js`
✅ `json-value-boolean.js`
✅ `json-value-null.js`
✅ `json-value-number.js`
✅ `json-extensibility-object.js`
✅ `json-idempotency.js`
✅ `json-via-namespace.js`
