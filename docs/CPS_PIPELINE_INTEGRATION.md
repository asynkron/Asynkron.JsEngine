# CPS Transformation Pipeline Integration - Quick Reference

## Where Does CPS Transformation Happen?

**Answer: Directly after the initial parse, before evaluation.**

```
┌─────────────────┐
│ JavaScript      │
│ Source Code     │
└────────┬────────┘
         │
         ▼
    ┌────────┐
    │ Lexer  │
    └────┬───┘
         │
         ▼
    ┌────────┐
    │ Parser │
    └────┬───┘
         │
         ▼
┌────────────────────┐
│ S-Expression Tree  │
│ (Direct Style)     │
└──────────┬─────────┘
           │
           ▼
    ┌──────────────────────────┐
    │  CPS Transformer         │  ← INSERT HERE
    │  (Selective, only for    │
    │   async/generator funcs) │
    └──────────┬───────────────┘
               │
               ▼
    ┌──────────────────────┐
    │ S-Expression Tree    │
    │ (CPS Style)          │
    └──────────┬───────────┘
               │
               ▼
         ┌──────────┐
         │ Evaluator│
         └──────┬───┘
                │
                ▼
            ┌────────┐
            │ Result │
            └────────┘
```

## Why This Position?

### ✅ Advantages of Post-Parse CPS Transformation

1. **Clean Separation of Concerns**
   - Parser: JavaScript → S-expressions (syntax)
   - CPS Transformer: Direct-style → CPS (semantics)
   - Evaluator: S-expressions → values (execution)

2. **Independent Testing**
   - Can test parser output separately
   - Can test CPS transformation separately
   - Can test evaluator separately

3. **Selective Application**
   - Only transform functions that need it (async/generator)
   - Synchronous code has zero overhead
   - Easy to detect which functions need transformation

4. **Preservation of Original AST**
   - Can keep original S-expression for debugging
   - Can compare before/after transformation
   - Easier to implement source maps

5. **Evaluator Simplicity**
   - Evaluator just executes CPS code
   - No need to mix interpretation with transformation
   - Cleaner code, easier to maintain

### ❌ Why Not During Parsing?

- Parser is already complex enough
- Would need to handle CPS concerns during syntax analysis
- Harder to test and debug
- Loss of separation between syntax and semantics

### ❌ Why Not During Evaluation?

- Would significantly complicate the evaluator
- Mix interpretation with transformation
- Harder to test and debug
- Performance overhead on every evaluation

## Implementation in JsEngine.cs

```csharp
public sealed class JsEngine
{
    private readonly CpsTransformer _cpsTransformer = new();
    
    /// <summary>
    /// Parses JavaScript source code into an S-expression representation.
    /// Optionally applies CPS transformation for async/generator functions.
    /// </summary>
    public Cons Parse(string source)
    {
        // Step 1: Tokenize
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        
        // Step 2: Parse to S-expressions
        var parser = new Parser(tokens);
        var program = parser.ParseProgram();
        
        // Step 3: Apply CPS transformation if needed
        if (_cpsTransformer.NeedsTransformation(program))
        {
            return _cpsTransformer.Transform(program);
        }
        
        return program;
    }
    
    /// <summary>
    /// Parses and immediately evaluates the provided source.
    /// </summary>
    public object? Evaluate(string source)
        => Evaluate(Parse(source));  // Parse handles CPS transformation
        
    /// <summary>
    /// Evaluates an S-expression program (may be in CPS style).
    /// </summary>
    public object? Evaluate(Cons program)
        => Evaluator.EvaluateProgram(program, _global);
}
```

## Detection Strategy

The transformer decides which functions need CPS transformation:

```csharp
public class CpsTransformer
{
    public bool NeedsTransformation(Cons program)
    {
        // Check if program contains any:
        // - async function declarations
        // - generator functions (function*)
        // - await expressions
        // - yield expressions
        
        return ContainsAsyncOrGenerator(program);
    }
    
    private bool ContainsAsyncOrGenerator(object? expr)
    {
        // Recursively search S-expression tree
        // Return true if any async/generator constructs found
    }
}
```

## Selective Transformation Example

```javascript
// This function stays unchanged (no transformation)
function add(a, b) {
    return a + b;
}

// This function gets CPS transformation
async function fetchData() {
    let result = await fetch();
    return result;
}

// This function gets CPS transformation
function* counter() {
    let i = 0;
    while (true) {
        yield i;
        i = i + 1;
    }
}
```

After parsing and transformation:
```
Program:
  - add: Original S-expression (direct style)
  - fetchData: Transformed S-expression (CPS style)
  - counter: Transformed S-expression (CPS style)
```

## Key Transformation Rules (Quick Reference)

### Literals
```scheme
; Before: 42
; After:  (call-k k 42)
```

### Variables
```scheme
; Before: x
; After:  (call-k k x)
```

### Binary Operations
```scheme
; Before: (+ a b)
; After:  (transform a (lambda (v1)
;           (transform b (lambda (v2)
;             (call-k k (+ v1 v2))))))
```

### Function Calls
```scheme
; Before: (call f arg)
; After:  (transform f (lambda (v-func)
;           (transform arg (lambda (v-arg)
;             (call v-func v-arg k)))))
```

### Await Expression
```scheme
; Before: (await promise)
; After:  (suspend-async promise k)
```

### Yield Expression
```scheme
; Before: (yield value)
; After:  (suspend value k)
```

## Performance Impact

| Code Type | Transformation | Overhead |
|-----------|----------------|----------|
| Synchronous functions | None | 0% |
| Async functions | Full CPS | ~15-30% |
| Generator functions | Full CPS | ~20-40% |
| Mixed code | Selective | Varies |

The overhead is acceptable because:
1. Only async/generator code is transformed
2. Regular synchronous code has zero overhead
3. Enables features that wouldn't be possible otherwise

## Summary

**When**: After parsing, before evaluation  
**Where**: In `JsEngine.Parse()` method  
**How**: Selective transformation based on function type  
**Why**: Clean separation, testability, performance  

For full details, see [CPS_TRANSFORMATION_PLAN.md](./CPS_TRANSFORMATION_PLAN.md).
