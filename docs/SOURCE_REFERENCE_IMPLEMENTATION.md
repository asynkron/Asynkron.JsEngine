# Source References and Transformation Tracking Implementation

## Summary

This PR successfully implements source reference tracking and transformation origin tracking for S-expressions in the Asynkron.JsEngine project, as requested in the issue.

## Key Features Implemented

### 1. Source Reference Tracking

Every S-expression (Cons) can now capture information about where it came from in the original JavaScript source code:

- **SourceReference class**: Stores the exact source location including:
  - Start and end positions (0-based character indices)
  - Start and end line/column numbers (1-based)
  - Reference to the original source text
  - `GetText()` method to retrieve the exact source code snippet

Example usage:
```csharp
var parsed = engine.ParseWithoutTransformation(source);
var forStatement = parsed.Rest.Head as Cons;
var sourceText = forStatement.SourceReference?.GetText();
// Returns: "for (let i = 0; i < 5; i++) { ... }"
```

### 2. Transformation Origin Tracking

Every transformed S-expression can now track back to its original form:

- **Origin property**: Points to the original Cons that this was transformed from
- Supports multi-level transformation tracking: `cons.Origin.Origin.Origin...`
- `null` Origin indicates an untransformed node

Example usage:
```csharp
var (original, transformed) = engine.ParseWithTransformationSteps(source);
var transformedFunc = transformed.Rest.Head as Cons;
var originalFunc = transformedFunc.Origin; // Points back to original async function
var sourceLocation = originalFunc.SourceReference; // Can trace to source
```

## Changes Made

### Core Infrastructure

1. **Token.cs**: Extended to include `StartPosition` and `EndPosition` for precise source tracking
2. **SourceReference.cs**: New class for storing and retrieving source location information
3. **Cons.cs**: Added two new properties:
   - `SourceReference?: SourceReference` - Points to source code location
   - `Origin?: Cons` - Points to pre-transformation s-expression
   - Helper methods: `WithSourceReference()` and `WithOrigin()`

### Parser Updates

4. **Lexer.cs**: Modified to track start line and column for each token
5. **Parser.cs**: 
   - Updated constructor to accept source string
   - Added `MakeCons()` helper methods to create Cons with source references
   - Applied to `ParseForStatement()` and `ParseAsyncFunctionDeclaration()` as examples

### Transformer Updates

6. **CpsTransformer.cs**: 
   - Added `MakeTransformedCons()` helper to set Origin when creating transformed nodes
   - Applied to `TransformAsyncFunction()` as example

7. **JsEngine.cs**: Updated to pass source string to Parser

## Testing

### Test Coverage
- **SourceReferenceTests.cs**: 4 tests covering source reference capture and retrieval
- **TransformationOriginTests.cs**: 5 tests covering transformation tracking
- All 9 new tests pass ✓
- All existing tests still pass (1047 passing, 9 pre-existing failures unrelated to this change)

### Demo Application
Created `examples/SourceReferenceDemo` showing:
- Source reference capture on for loops
- Transformation tracking on async functions
- Tracing from transformed code back to original source

## Benefits

1. **Better Debugging**: The new `__debug()` feature can now show exact source locations
2. **Transformation Tracing**: Developers can trace transformed code back through multiple transformation levels
3. **Error Reporting**: Future error messages can include precise source locations
4. **Minimal Changes**: Implementation uses targeted changes to key parsing/transformation points

## Example Output

Running the demo shows:

```
Example 1: Source References on For Loops
-----------------------------------------
For loop location: [2:1 - 4:1]
Captured source text:
for (let i = 0; i < 5; i++) {
    console.log(i);
}

Example 2: Transformation Tracking
-----------------------------------
Transformation Chain:
- Original function Origin: null (not transformed)
- Transformed function Origin: points back to original
- Verified: Transformed points to original: True
- Can trace back to source via origin: [2:1 - 5:1]
```

## Security

- CodeQL security scan: 0 alerts ✓
- No new security vulnerabilities introduced

## Future Enhancements

The infrastructure is now in place to:
1. Add source references to more parse methods (currently only for loops and async functions)
2. Add origin tracking to more transformation methods (currently only async function transformation)
3. Extend `__debug()` to use these references for better debugging information
4. Improve error messages with precise source locations

## Files Changed

- `src/Asynkron.JsEngine/SourceReference.cs` (new)
- `src/Asynkron.JsEngine/Token.cs` (modified)
- `src/Asynkron.JsEngine/Lexer.cs` (modified)
- `src/Asynkron.JsEngine/Cons.cs` (modified)
- `src/Asynkron.JsEngine/Parser.cs` (modified)
- `src/Asynkron.JsEngine/CpsTransformer.cs` (modified)
- `src/Asynkron.JsEngine/JsEngine.cs` (modified)
- `tests/Asynkron.JsEngine.Tests/SourceReferenceTests.cs` (new)
- `tests/Asynkron.JsEngine.Tests/TransformationOriginTests.cs` (new)
- `examples/SourceReferenceDemo/` (new demo project)
