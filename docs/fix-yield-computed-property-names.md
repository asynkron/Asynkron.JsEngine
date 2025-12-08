# Fix Yield in Class Computed Property Names

## Problem Summary

Generator functions containing class declarations with `yield` expressions in computed property names fail. The `SyncGeneratorIrBuilder` currently rejects class declarations that contain `yield` in computed property names or the `extends` clause, causing test failures.

## Hard Facts

- Computed property names in class definitions are evaluated in the generator's execution context
- `yield` expressions in computed property names must suspend the generator
- ECMAScript spec: ClassElementName is evaluated during class definition evaluation, which happens in the generator body
- `GeneratorYieldLowerer` already handles simple `YieldExpression` nodes but not complex yield-containing expressions
- `SyncGeneratorIrBuilder.ClassDefinitionContainsYield()` returns `true` if any computed name contains yield, causing the builder to fail

## Test Status

**Failing Tests** (from languagetests.md):
- `language/statements/class/cpn-class-decl-computed-property-name-from-yield-expression.js`
- `language/statements/class/cpn-class-decl-accessors-computed-property-name-from-yield-expression.js`
- `language/statements/class/accessor-name-inst-computed-yield-expr.js`
- `language/statements/class/accessor-name-static-computed-yield-expr.js`
- `language/expressions/object/cpn-obj-lit-computed-property-name-from-yield-expression.js`
- `language/expressions/object/method-definition/computed-property-name-yield-expression.js`

## What We Have

### Current Implementation in GeneratorYieldLowerer.cs

The `TryRewriteClassDefinition` method (lines 233-281) handles simple cases:
```csharp
if (member is { IsComputed: true, ComputedName: YieldExpression computedYield })
{
    var tempBinding = CreateResumeIdentifier();
    prefixStatements.Add(CreateYieldDeclaration(computedYield.Source, tempBinding, computedYield));
    var replacement = new IdentifierExpression(computedYield.Source, tempBinding.Name);
    members[i] = member with { ComputedName = replacement };
    changed = true;
}
```

This only matches when `ComputedName` is **directly** a `YieldExpression`, not when yield is nested inside another expression.

### Current Implementation in SyncGeneratorIrBuilder.cs

The builder rejects class declarations with yield in computed names (lines 332-344):
```csharp
case ClassDeclaration classDeclaration:
    if (ClassDefinitionContainsYield(classDeclaration.Definition))
    {
        entryIndex = -1;
        _failureReason ??= "Class declaration contains yield in computed property names or extends clause.";
        return false;
    }
    entryIndex = Append(new StatementInstruction(nextIndex, classDeclaration));
    return true;
```

## Why It's Failing

1. **Simple `yield` as computed name works**: `class C { [yield] m() {} }` - the `YieldExpression` is the direct `ComputedName`
2. **Complex expressions with yield fail**: `class C { [yield 1] m() {} }` - the yield is part of a larger expression tree
3. **The GeneratorYieldLowerer checks for direct YieldExpression only**, not for nested yields
4. **The SyncGeneratorIrBuilder sees yield via `AstShapeAnalyzer.ContainsYield()` and rejects the class**

## Solution Path

### Option 1: Extend GeneratorYieldLowerer to Handle Complex Yields

Modify `TryRewriteClassDefinition` to use `RewriteExpressionForComplexYields` for computed property names:

```csharp
for (var i = 0; i < members.Count; i++)
{
    var member = members[i];
    if (member.IsComputed && member.ComputedName is not null &&
        AstShapeAnalyzer.ContainsYield(member.ComputedName))
    {
        // Handle complex expressions that contain yield
        var rewrittenExpr = RewriteExpressionForComplexYields(
            member.ComputedName, prefixStatements, ref changed);
        members[i] = member with { ComputedName = rewrittenExpr };
    }
}
```

### Option 2: Handle Yields in SyncGeneratorIrBuilder

Instead of rejecting class declarations with yields, emit instructions to:
1. Evaluate each yield-containing computed name
2. Store the result in a temporary
3. Proceed with the class declaration using the temporary values

### Option 3: Lower Class Declarations Before IR Building

Apply the `GeneratorYieldLowerer` transformation pass before `SyncGeneratorIrBuilder` runs, so by the time IR is built, all yields in computed names have been hoisted.

## Recommended Approach

**Option 1** is the most straightforward:
1. Enhance `GeneratorYieldLowerer.TryRewriteClassDefinition()` to detect and rewrite **any** computed name containing a yield (not just direct `YieldExpression`)
2. Use `AstShapeAnalyzer.ContainsYield()` to detect yields
3. Use `RewriteExpressionForComplexYields()` to hoist the yield into a prefix statement

## Key Files

- `src/Asynkron.JsEngine/Execution/SyncGeneratorIrBuilder.cs` - IR builder that rejects classes with yield
- `src/Asynkron.JsEngine/Execution/GeneratorYieldLowerer.cs` - AST transformation for yield expressions
- `src/Asynkron.JsEngine/Ast/AstShapeAnalyzer.cs` - Contains `ContainsYield()` method for detecting yields in expressions

## Implementation Steps

1. Modify `TryRewriteClassDefinition` in `GeneratorYieldLowerer.cs`:
   - Change detection from checking for direct `YieldExpression` to using `AstShapeAnalyzer.ContainsYield()`
   - Apply `RewriteExpressionForComplexYields()` to computed names containing yields

2. Handle the `extends` clause similarly if it contains yields:
   - Hoist yield from extends expression to prefix statements
   - Replace with a temporary identifier

3. Ensure the lowering pass runs before IR building so `SyncGeneratorIrBuilder` sees already-transformed classes

4. Test with the failing Test262 tests to verify the fix

## Fix Applied (December 8, 2025)

**All 15 yield-in-computed-property-name tests now pass!**

### Changes Made

1. **`src/Asynkron.JsEngine/Execution/GeneratorYieldLowerer.cs`**:

   **Modified `TryRewriteClassDefinition()`** to use `AstShapeAnalyzer.ContainsYield()` instead of checking for direct `YieldExpression`:
   ```csharp
   // Handle extends clause containing yield
   if (definition.Extends is not null && AstShapeAnalyzer.ContainsYield(definition.Extends))
   {
       var extendsChanged = false;
       rewrittenExtends = RewriteExpressionForComplexYields(definition.Extends, prefixStatements, ref extendsChanged);
       changed |= extendsChanged;
   }

   for (var i = 0; i < members.Count; i++)
   {
       var member = members[i];
       if (member.IsComputed && member.ComputedName is not null &&
           AstShapeAnalyzer.ContainsYield(member.ComputedName))
       {
           var memberChanged = false;
           var rewrittenName = RewriteExpressionForComplexYields(member.ComputedName, prefixStatements, ref memberChanged);
           members[i] = member with { ComputedName = rewrittenName };
           changed |= memberChanged;
       }
   }
   ```

   **Added `TryRewriteClassDeclaration()`** to handle class declaration statements:
   ```csharp
   private bool TryRewriteClassDeclaration(
       StatementNode statement,
       out ImmutableArray<StatementNode> replacement)
   {
       // ...handles ClassDeclaration with yield in computed names
   }
   ```

   **Added object literal support** via new methods:
   - `TryRewriteObjectLiteralUsage()` - dispatch method
   - `TryRewriteObjectLiteralDeclaration()` - handles `let o = { [yield 9]: 9 }`
   - `TryRewriteObjectLiteralExpression()` - handles expression statements with object literals
   - `TryRewriteObjectExpression()` - rewrites object expressions with yield in computed keys

### Key Insight

The fix ensures that:
1. **Class declarations/expressions**: Both class declarations (`class C { [yield 1] m() {} }`) and class expressions are now handled
2. **Object literals**: Object expressions like `{ [yield 9]: 9 }` in variable declarations are now transformed
3. **Extends clause**: The extends clause in class definitions is also checked for yields and transformed if needed
4. **Fields**: Class fields with computed names containing yields are handled

The transformation hoists yield expressions into prefix variable declarations before the class/object declaration, so by the time `SyncGeneratorIrBuilder` sees the class, the yields have been replaced with simple identifier references.
