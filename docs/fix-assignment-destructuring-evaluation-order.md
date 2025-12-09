# Fix Expressions_assignment_destructuring Iterator Tests

## Problem Summary
The test `iterator-destructuring-property-reference-target-evaluation-order.js` is failing because the engine is incorrectly throwing `TypeError: Cannot reassign constant 'target'` when a function-scoped `function target() {...}` tries to reassign itself.

## Hard Facts

- Function declarations in sloppy mode are hoisted as `var` bindings, not `const` bindings
- The test code does: `return target = {...}` inside a function named `target`
- In sloppy mode, this should be allowed because `target` is effectively a `var`
- The test expects this evaluation order for `([target()[targetKey()]] = source())`:
  1. `source()` called first → returns an iterable
  2. `[Symbol.iterator]()` called → returns the iterator
  3. `target()` called → evaluates the target object expression
  4. `targetKey()` called → evaluates the property key expression
  5. `iterator.next()` called → gets the first value
  6. `done` getter called → returns true (no value to assign)
  7. `toString()` called on targetKey result → gets the property name 'q'
  8. `set q(v)` called → assigns the value

**Expected log:** `["source","iterator","target","target-key","iterator-step","iterator-done","target-key-tostring","set"]`

## Test Status

- **2/15 destructuring tests fail**: `iterator-destructuring-property-reference-target-evaluation-order.js` (strict and non-strict)
- Cannot even run the test because of the `Cannot reassign constant` error

## Root Cause Analysis

### Immediate Error
The engine is treating `function target()` as a const binding, preventing `target = {...}` from working.

This is likely happening in one of these places:
- `src/Asynkron.JsEngine/JsEngine.cs` - during hoisting of function declarations
- `src/Asynkron.JsEngine/JsEnvironment.cs` - during binding creation
- Function declarations may be creating `const` bindings instead of mutable `var` bindings

### Evaluation Order Issue (Secondary)
Even after fixing the const issue, there may be an evaluation order problem. Per ES spec, for array destructuring assignment with computed properties:

1. Evaluate the RHS (source expression) first
2. Get the iterator from the RHS result
3. For each element pattern in the LHS:
   a. Evaluate the target reference BEFORE getting the next iterator value
   b. Get the next iterator value
   c. Perform the assignment

The current implementation in `ArrayBindingExtensions.cs` already has logic to pre-resolve references before calling `iterator.next()`:
```csharp
if (mode == BindingMode.Assign && element.Target is AssignmentTargetBinding assignmentTarget)
{
    preResolvedReference = AssignmentReferenceResolver.ResolveForDestructuring(
        assignmentTarget.Expression,
        environment,
        context,
        EvaluateExpression);
    // ... then later calls iteratorRecord.Next()
}
```

## Key Files

- `src/Asynkron.JsEngine/JsEngine.cs` - function declaration hoisting
- `src/Asynkron.JsEngine/JsEnvironment.cs` - binding creation and mutability flags
- `src/Asynkron.JsEngine/Ast/ArrayBindingExtensions.cs` - array destructuring evaluation order
- `src/Asynkron.JsEngine/Ast/AssignmentReference.cs` - deferred property key conversion

## What We Should Try Next

### Fix 1: Function Declaration Mutability
1. Find where function declarations are hoisted
2. Ensure they create `var` bindings (mutable) not `const` bindings
3. Check `JsEnvironment.DeclareBinding` or similar for the mutability flag

### Fix 2: Verify Evaluation Order
After fixing the const issue:
1. Run the playground test to see actual vs expected order
2. If order is wrong, trace through `ArrayBindingExtensions.cs`
3. Ensure `ResolveForDestructuring` with `deferPropertyKeyConversion: true` defers `toString()` until SetValue time

## Playground Test Code

```csharp
var code = """
var log = [];
function source() {
    log.push('source');
    var iterator = {
        next: function() {
            log.push('iterator-step');
            return {
                get done() {
                    log.push('iterator-done');
                    return true;
                },
                get value() {
                    log.push('iterator-value');
                    return 1;
                }
            };
        }
    };
    var source = {};
    source[Symbol.iterator] = function() {
        log.push('iterator');
        return iterator;
    };
    return source;
}
function target() {
    log.push('target');
    return target = {  // <-- This line fails with "Cannot reassign constant 'target'"
        set q(v) {
            log.push('set');
        }
    };
}
function targetKey() {
    log.push('target-key');
    return {
        toString: function() {
            log.push('target-key-tostring');
            return 'q';
        }
    };
}

([target()[targetKey()]] = source());

console.log('Actual:', JSON.stringify(log));
console.log('Expected:', JSON.stringify([
    'source', 'iterator',
    'target', 'target-key',
    'iterator-step', 'iterator-done',
    'target-key-tostring', 'set',
]));
""";
```

## Investigation Notes

### Finding the const binding issue

Need to trace:
1. How `function target()` gets hoisted
2. What binding type is created (var vs const vs let)
3. Why the engine thinks it's a constant

Look for:
- `VariableKind` enum usage
- `BindingKind` or `DeclarationKind`
- `IsMutable` or `IsConst` flags on bindings
