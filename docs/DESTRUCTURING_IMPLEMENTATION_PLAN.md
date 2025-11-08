# Destructuring Implementation Plan

## Overview

Destructuring is a JavaScript feature that allows unpacking values from arrays or properties from objects into distinct variables. This document outlines the implementation plan for adding destructuring support to Asynkron.JsEngine.

## Current Status

✅ **Completed:**
- String methods (18 methods, 34 tests)
- Regular expressions (RegExp constructor, test/exec methods, 19 tests)
- Async/await and generators (via CPS transformation)

🚧 **Remaining:**
- Array destructuring
- Object destructuring
- Destructuring in function parameters

## Destructuring Syntax Examples

### Array Destructuring

```javascript
// Basic array destructuring
let [a, b] = [1, 2];

// Skipping elements
let [first, , third] = [1, 2, 3];

// Rest elements
let [head, ...tail] = [1, 2, 3, 4];

// Default values
let [a = 10, b = 20] = [5];

// Nested destructuring
let [a, [b, c]] = [1, [2, 3]];
```

### Object Destructuring

```javascript
// Basic object destructuring
let {x, y} = {x: 1, y: 2};

// Renaming
let {x: newX, y: newY} = {x: 1, y: 2};

// Default values
let {x = 10, y = 20} = {x: 5};

// Nested destructuring
let {a, b: {c}} = {a: 1, b: {c: 2}};

// Rest properties
let {x, ...rest} = {x: 1, y: 2, z: 3};
```

### Function Parameter Destructuring

```javascript
// Array destructuring in parameters
function sum([a, b]) {
    return a + b;
}

// Object destructuring in parameters
function greet({name, age}) {
    return `Hello ${name}, you are ${age}`;
}

// With defaults
function configure({timeout = 1000, retries = 3} = {}) {
    // ...
}
```

## Implementation Steps

### Phase 1: Parser Changes

#### 1.1 Lexer (No changes needed)
Current lexer already supports all required tokens (`[`, `]`, `{`, `}`, `,`, `:`, `...`).

#### 1.2 Parser - Array Destructuring

Add new parsing method in `Parser.cs`:

```csharp
private object ParseArrayDestructuringPattern()
{
    // Parse: [a, b, ...rest]
    // Returns S-expression: (array-pattern (name a) (name b) (rest rest))
}
```

Modify `ParseVariableDeclaration()` to detect destructuring:

```csharp
private object ParseVariableDeclaration(TokenType kind)
{
    // Check for destructuring pattern
    if (Check(TokenType.LeftBracket))
    {
        return ParseArrayDestructuring(kind);
    }
    else if (Check(TokenType.LeftBrace))
    {
        return ParseObjectDestructuring(kind);
    }
    
    // Existing simple variable logic...
}
```

#### 1.3 Parser - Object Destructuring

Add new parsing method:

```csharp
private object ParseObjectDestructuringPattern()
{
    // Parse: {x, y: z, ...rest}
    // Returns S-expression: (object-pattern (prop x x) (prop y z) (rest rest))
}
```

### Phase 2: S-Expression Extensions

Add new symbols to `JsSymbols.cs`:

```csharp
public static readonly Symbol ArrayPattern = Intern("array-pattern");
public static readonly Symbol ObjectPattern = Intern("object-pattern");
public static readonly Symbol DestructureElement = Intern("element");
public static readonly Symbol DestructureProperty = Intern("property");
public static readonly Symbol DestructureRest = Intern("rest");
public static readonly Symbol DestructureDefault = Intern("default");
```

### Phase 3: Evaluator Changes

#### 3.1 Array Destructuring Evaluation

Add to `Evaluator.cs`:

```csharp
private static object? EvaluateArrayDestructuring(Cons cons, Environment environment, TokenType kind)
{
    // Extract pattern and initializer
    var pattern = ExpectCons(cons.Rest.Head, "Expected array pattern");
    var initExpression = cons.Rest.Rest.Head;
    var value = EvaluateExpression(initExpression, environment);
    
    // Value must be array-like
    if (value is not JsArray array)
    {
        throw new InvalidOperationException("Cannot destructure non-array value");
    }
    
    // Iterate through pattern elements
    int index = 0;
    foreach (var element in pattern.Rest)
    {
        if (IsRestElement(element))
        {
            // Collect remaining elements into rest array
            var rest = new JsArray();
            for (int i = index; i < array.Items.Count; i++)
            {
                rest.Push(array.Items[i]);
            }
            DefineVariable(kind, GetRestName(element), rest, environment);
            break;
        }
        else
        {
            var name = GetElementName(element);
            var elementValue = index < array.Items.Count 
                ? array.Items[index] 
                : GetDefaultValue(element);
            DefineVariable(kind, name, elementValue, environment);
            index++;
        }
    }
    
    return null;
}
```

#### 3.2 Object Destructuring Evaluation

```csharp
private static object? EvaluateObjectDestructuring(Cons cons, Environment environment, TokenType kind)
{
    // Extract pattern and initializer
    var pattern = ExpectCons(cons.Rest.Head, "Expected object pattern");
    var initExpression = cons.Rest.Rest.Head;
    var value = EvaluateExpression(initExpression, environment);
    
    // Value must be object-like
    if (value is not JsObject obj)
    {
        throw new InvalidOperationException("Cannot destructure non-object value");
    }
    
    // Iterate through pattern properties
    foreach (var prop in pattern.Rest)
    {
        if (IsRestProperty(prop))
        {
            // Collect remaining properties into rest object
            var rest = new JsObject();
            var usedKeys = GetUsedKeys(pattern);
            foreach (var kvp in obj)
            {
                if (!usedKeys.Contains(kvp.Key))
                {
                    rest[kvp.Key] = kvp.Value;
                }
            }
            DefineVariable(kind, GetRestName(prop), rest, environment);
            break;
        }
        else
        {
            var (sourceName, targetName) = GetPropertyNames(prop);
            var propValue = obj.TryGetProperty(sourceName, out var val) 
                ? val 
                : GetDefaultValue(prop);
            DefineVariable(kind, targetName, propValue, environment);
        }
    }
    
    return null;
}
```

### Phase 4: Function Parameter Destructuring

Modify function parameter parsing in `Parser.cs`:

```csharp
private (List<Symbol>, Symbol?) ParseParameterList(Cons parameters)
{
    var regularParams = new List<Symbol>();
    Symbol? restParam = null;
    
    foreach (var param in parameters)
    {
        if (param is Cons patternCons)
        {
            // Destructuring parameter - store the pattern
            regularParams.Add(EncodeDestructuringParam(patternCons));
        }
        else if (param is Symbol symbol)
        {
            // Regular parameter
            regularParams.Add(symbol);
        }
    }
    
    return (regularParams, restParam);
}
```

Modify function invocation to handle destructuring parameters:

```csharp
// In function call, before executing body
for (int i = 0; i < paramNames.Count; i++)
{
    var paramName = paramNames[i];
    var argValue = i < args.Count ? args[i] : null;
    
    if (IsDestructuringParam(paramName))
    {
        // Perform destructuring assignment
        var pattern = DecodeDestructuringParam(paramName);
        DestructureValue(pattern, argValue, localEnv, TokenType.Let);
    }
    else
    {
        // Regular parameter assignment
        localEnv.Define(paramName, argValue);
    }
}
```

## Testing Strategy

### Unit Tests

Create `DestructuringTests.cs` with test cases for:

1. **Array Destructuring (15 tests)**
   - Basic array destructuring
   - Nested array destructuring
   - Array destructuring with defaults
   - Array destructuring with rest elements
   - Array destructuring with skip elements
   - Assignment to existing variables

2. **Object Destructuring (15 tests)**
   - Basic object destructuring
   - Object destructuring with renaming
   - Nested object destructuring
   - Object destructuring with defaults
   - Object destructuring with rest properties
   - Mixed array and object destructuring

3. **Function Parameters (10 tests)**
   - Array destructuring in parameters
   - Object destructuring in parameters
   - Destructuring with defaults in parameters
   - Multiple destructured parameters
   - Rest parameters with destructuring

4. **Edge Cases (10 tests)**
   - Destructuring null/undefined
   - Destructuring with const
   - Destructuring in for loops
   - Nested function destructuring
   - Complex real-world patterns

## Estimated Effort

- **Parser changes**: 200-300 lines
- **Evaluator changes**: 300-400 lines
- **Test cases**: 200-300 lines
- **Documentation**: 100 lines
- **Total**: ~800-1200 lines of code

**Time estimate**: 8-12 hours for a developer familiar with the codebase

## Benefits vs. Complexity

**Benefits:**
- More idiomatic JavaScript code
- Reduces boilerplate in common patterns
- Enables modern JavaScript patterns

**Complexity:**
- Significant parser changes required
- Additional S-expression forms
- Complex evaluation logic
- Extensive test coverage needed

## Recommendation

Destructuring is a valuable feature but requires substantial implementation effort. Given that string methods and regex provide more immediate practical value with less complexity, they were prioritized. Destructuring should be the next major feature after the current implementation is stable.

## Alternative Approach

Consider implementing a simplified version first:
1. Basic array destructuring only (no nesting, no rest, no defaults)
2. Basic object destructuring only (no renaming, no rest, no defaults)
3. Add advanced features incrementally

This phased approach would provide value sooner while managing complexity.
