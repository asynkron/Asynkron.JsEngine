# CPS Transformation Plan for Generators and Async/Await

## Executive Summary

This document outlines a detailed plan for implementing Continuation-Passing Style (CPS) transformation in the Asynkron.JsEngine to support generators (`function*`, `yield`) and async/await functionality. The CPS transformation will convert the S-expression intermediate representation into a form where control flow is explicitly managed through continuations, enabling non-blocking execution and stateful resumption.

## Table of Contents

1. [Background](#background)
2. [CPS Transformation Fundamentals](#cps-transformation-fundamentals)
3. [Integration Point in Pipeline](#integration-point-in-pipeline)
4. [Transformation Strategy](#transformation-strategy)
5. [Implementation Phases](#implementation-phases)
6. [S-Expression Extensions](#s-expression-extensions)
7. [Examples](#examples)
8. [Testing Strategy](#testing-strategy)
9. [Performance Considerations](#performance-considerations)
10. [Migration Path](#migration-path)

## Background

### Current Architecture

The Asynkron.JsEngine operates in three phases:

```
JavaScript Source → Lexer → Parser → S-Expression Tree → Evaluator → Result
```

Currently, the evaluator directly interprets S-expressions in a synchronous, recursive manner. This approach makes it impossible to:
- Pause and resume execution (generators)
- Yield control back to the event loop (async/await)
- Manage asynchronous operations without blocking

### What is CPS?

Continuation-Passing Style (CPS) is a programming style where control flow is made explicit through continuation functions. Instead of returning values directly, functions accept an additional "continuation" parameter that represents "what to do next."

**Direct Style:**
```javascript
let x = 1 + 2;
let y = x * 3;
return y;
```

**CPS Style:**
```javascript
add(1, 2, function(x) {
  multiply(x, 3, function(y) {
    return_k(y);
  });
});
```

## CPS Transformation Fundamentals

### Core Principles

1. **Every expression produces a value through a continuation**
   - Instead of `expr → value`, we have `transform(expr, k) → k(value)`
   
2. **Control flow becomes explicit**
   - If/else, loops, and function calls all pass control through continuations
   
3. **Tail call optimization becomes natural**
   - All calls in CPS are tail calls, making the transformation efficient

### Key Benefits for Our Use Case

1. **Generators**: Can pause execution by capturing the current continuation
2. **Async/Await**: Can suspend and resume at await points
3. **Explicit Control**: Makes debugging and profiling easier
4. **Non-blocking**: Enables yielding to the event loop

## Integration Point in Pipeline

### Recommended Integration Point: After Parsing, Before Evaluation

```
JavaScript Source 
  ↓ Lexer
Tokens
  ↓ Parser
S-Expression Tree (Direct Style)
  ↓ CPS Transformer ← INSERT HERE
S-Expression Tree (CPS Style)
  ↓ Evaluator
Result
```

**Rationale:**

1. **After Parsing**: The S-expression tree is complete and validated
2. **Before Evaluation**: The evaluator can remain relatively simple
3. **Selective Application**: Only async/generator functions need transformation
4. **Testability**: Can test CPS transformation independently

### Alternative Considered: During Evaluation

This was rejected because:
- Would complicate the evaluator significantly
- Harder to test and debug
- Mixing concerns (interpretation + transformation)
- No clear separation of phases

## Transformation Strategy

### Two-Tier Approach

We'll implement a **selective CPS transformation**:

1. **Synchronous Code**: Evaluated directly (no transformation)
2. **Async/Generator Code**: Transformed to CPS and evaluated with continuation support

This hybrid approach maintains performance for synchronous code while enabling advanced features where needed.

### Detection Rules

Transform a function to CPS if:
- It's declared with `async` keyword
- It's declared with `function*` (generator)
- It contains `await` expressions
- It contains `yield` expressions
- It calls another async/generator function (transitive)

### Transformation Algorithm

The core transformation takes an S-expression and a continuation symbol:

```
transform(expr, k) → CPS-expr

Where:
- expr: Original S-expression
- k: Symbol representing the continuation function
- CPS-expr: Transformed S-expression in CPS
```

### Basic Transformation Rules

#### 1. Literals and Variables

```scheme
; Original: 42
transform(42, k) → (call k 42)

; Original: x
transform(x, k) → (call k x)
```

#### 2. Binary Operations

```scheme
; Original: (+ a b)
transform((+ a b), k) →
  (transform a (lambda (v1)
    (transform b (lambda (v2)
      (call k (+ v1 v2))))))
```

#### 3. Function Calls

```scheme
; Original: (call f arg)
transform((call f arg), k) →
  (transform f (lambda (v-func)
    (transform arg (lambda (v-arg)
      (call v-func v-arg k)))))
```

Note: Functions in CPS take an additional continuation parameter.

#### 4. Conditionals

```scheme
; Original: (if cond then else)
transform((if cond then else), k) →
  (transform cond (lambda (v-cond)
    (if v-cond
      (transform then k)
      (transform else k))))
```

#### 5. Sequences/Blocks

```scheme
; Original: (block stmt1 stmt2 ... stmtN)
transform((block s1 s2), k) →
  (transform s1 (lambda (_)
    (transform s2 k)))
```

#### 6. Assignments

```scheme
; Original: (assign x expr)
transform((assign x expr), k) →
  (transform expr (lambda (v)
    (block
      (assign x v)
      (call k v))))
```

#### 7. Loops (While)

Loops require special handling to allow suspension:

```scheme
; Original: (while condition body)
transform((while cond body), k) →
  (letrec ((loop (lambda (dummy)
                   (transform cond (lambda (v-cond)
                     (if v-cond
                       (transform body loop)
                       (call k null)))))))
    (loop null))
```

## Implementation Phases

### Phase 1: Foundation (Week 1-2)

**Goal**: Basic CPS infrastructure without async/await

1. **Create CpsTransformer class**
   ```csharp
   public class CpsTransformer
   {
       public Cons Transform(Cons expr, Symbol continuation);
       public bool NeedsTransformation(Cons expr);
   }
   ```

2. **Add new S-expression symbols**
   ```csharp
   public static readonly Symbol Continuation = Symbol.Intern("continuation");
   public static readonly Symbol Resume = Symbol.Intern("resume");
   public static readonly Symbol Suspend = Symbol.Intern("suspend");
   ```

3. **Implement transformations for**:
   - Literals and variables
   - Binary operations
   - Simple function calls
   - Conditionals
   - Blocks

4. **Testing**: Unit tests for each transformation rule

**Deliverables**:
- `CpsTransformer.cs` with core transformation logic
- Extended `JsSymbols.cs` with CPS-related symbols
- Unit tests for basic transformations

### Phase 2: Control Flow (Week 3-4)

**Goal**: Handle complex control flow

1. **Implement transformations for**:
   - While loops
   - For loops
   - Do-while loops
   - Break/Continue (captured as continuation jumps)
   - Try/Catch/Finally (error continuations)
   - Switch statements

2. **Add support for multiple continuations**:
   - Normal continuation (success path)
   - Error continuation (exception path)
   - Break continuation (loop exit)
   - Continue continuation (loop restart)

**Deliverables**:
- Complete control flow transformation
- Tests for loops with break/continue
- Exception handling in CPS

### Phase 3: Functions and Closures (Week 5-6)

**Goal**: Transform functions to accept continuations

1. **Transform function declarations**:
   ```scheme
   ; Original: (function f (x y) body)
   ; CPS: (function f (x y k) (transform body k))
   ```

2. **Handle closures properly**:
   - Captured variables remain captured
   - Continuations can be captured in closures

3. **Function expressions and lambdas**

**Deliverables**:
- Function transformation
- Closure handling
- Higher-order function tests

### Phase 4: Generators (Week 7-8)

**Goal**: Implement generator support

1. **Parse generator syntax**:
   ```javascript
   function* gen() {
       yield 1;
       yield 2;
   }
   ```

2. **Transform yield expressions**:
   ```scheme
   ; Original: (yield expr)
   ; CPS: (suspend expr (lambda (resume-value) ...))
   ```

3. **Create Generator object**:
   ```csharp
   public class JsGenerator : IJsCallable
   {
       private Cons _body;
       private Environment _environment;
       private object? _currentContinuation;
       
       public object? Next(object? value);
       public object? Return(object? value);
       public object? Throw(object? error);
   }
   ```

4. **Implement iterator protocol**:
   - `next()` method
   - `{value, done}` return format
   - `return()` and `throw()` methods

**Deliverables**:
- Generator function support
- Yield expression handling
- Iterator protocol implementation
- Generator tests

### Phase 5: Async/Await (Week 9-10)

**Goal**: Implement async/await

1. **Parse async syntax**:
   ```javascript
   async function fetchData() {
       let result = await fetch();
       return result;
   }
   ```

2. **Transform await expressions**:
   ```scheme
   ; Original: (await promise)
   ; CPS: (suspend-async promise (lambda (resolved-value) ...))
   ```

3. **Create Promise implementation**:
   ```csharp
   public class JsPromise
   {
       public void Then(IJsCallable onFulfilled, IJsCallable onRejected);
       public void Resolve(object? value);
       public void Reject(object? reason);
   }
   ```

4. **Integrate with continuation system**:
   - Await suspends and captures continuation
   - Promise resolution resumes continuation

**Deliverables**:
- Async function support
- Await expression handling
- Promise implementation
- Async/await tests

### Phase 6: Integration and Optimization (Week 11-12)

**Goal**: Polish and optimize

1. **Integrate with JsEngine**:
   ```csharp
   public Cons Parse(string source)
   {
       var program = parser.ParseProgram();
       
       // Apply CPS transformation if needed
       if (_cpsTransformer.NeedsTransformation(program))
       {
           return _cpsTransformer.Transform(program);
       }
       
       return program;
   }
   ```

2. **Optimize common patterns**:
   - Avoid transformation overhead for simple cases
   - Inline trivial continuations
   - Cache transformed functions

3. **Performance benchmarks**

**Deliverables**:
- Integrated CPS pipeline
- Performance benchmarks
- Documentation updates

## S-Expression Extensions

### New Symbols

```csharp
// CPS-related
public static readonly Symbol Continuation = Symbol.Intern("continuation");
public static readonly Symbol CallK = Symbol.Intern("call-k");
public static readonly Symbol LetK = Symbol.Intern("let-k");

// Async/Await
public static readonly Symbol Async = Symbol.Intern("async");
public static readonly Symbol Await = Symbol.Intern("await");
public static readonly Symbol Promise = Symbol.Intern("promise");

// Generators
public static readonly Symbol Generator = Symbol.Intern("generator");
public static readonly Symbol Yield = Symbol.Intern("yield");
public static readonly Symbol YieldStar = Symbol.Intern("yield*");

// Suspension/Resumption
public static readonly Symbol Suspend = Symbol.Intern("suspend");
public static readonly Symbol Resume = Symbol.Intern("resume");
```

### S-Expression Examples

#### CPS-Transformed Function
```scheme
; Original:
(function add (a b)
  (block
    (return (+ a b))))

; CPS-Transformed:
(function add (a b k)
  (block
    (call-k k (+ a b))))
```

#### Generator Function
```scheme
; Original:
(generator numbers ()
  (block
    (yield 1)
    (yield 2)
    (yield 3)))

; CPS-Transformed:
(generator numbers ()
  (block
    (suspend 1 (lambda (_)
      (suspend 2 (lambda (_)
        (suspend 3 (lambda (_)
          (return null)))))))))
```

#### Async Function
```scheme
; Original:
(async fetchUser (id)
  (block
    (let data (await (call fetch id)))
    (return data)))

; CPS-Transformed:
(async fetchUser (id k)
  (block
    (call fetch id (lambda (data)
      (call-k k data)))))
```

## Examples

### Example 1: Simple Arithmetic in CPS

**Original JavaScript:**
```javascript
let x = 1 + 2;
let y = x * 3;
return y;
```

**Original S-Expression:**
```scheme
(program
  (let x (+ 1 2))
  (let y (* x 3))
  (return y))
```

**CPS-Transformed S-Expression:**
```scheme
(program
  (call-k (lambda (v1)
    (assign x v1)
    (call-k (lambda (v2)
      (assign y v2)
      (call-k identity-k y))
    (* x 3)))
  (+ 1 2)))
```

### Example 2: Generator Function

**JavaScript:**
```javascript
function* counter() {
    let i = 0;
    while (true) {
        yield i;
        i = i + 1;
    }
}

let gen = counter();
gen.next(); // {value: 0, done: false}
gen.next(); // {value: 1, done: false}
```

**S-Expression (simplified):**
```scheme
(generator counter ()
  (block
    (let i 0)
    (while true
      (block
        (yield i)
        (assign i (+ i 1))))))
```

**CPS-Transformed (conceptual):**
```scheme
(generator counter ()
  (let-state ((i 0)
              (pc 0))  ; program counter for resumption
    (case pc
      (0 (block
           (suspend i (lambda (_)
             (set! pc 1)
             (resume)))))
      (1 (block
           (assign i (+ i 1))
           (set! pc 0)
           (resume))))))
```

### Example 3: Async/Await

**JavaScript:**
```javascript
async function fetchUserData(id) {
    let user = await fetchUser(id);
    let posts = await fetchPosts(user.id);
    return posts;
}
```

**S-Expression:**
```scheme
(async fetchUserData (id)
  (block
    (let user (await (call fetchUser id)))
    (let posts (await (call fetchPosts (get-prop user "id"))))
    (return posts)))
```

**CPS-Transformed:**
```scheme
(async fetchUserData (id k)
  (call fetchUser id (lambda (user)
    (call fetchPosts (get-prop user "id") (lambda (posts)
      (call-k k posts))))))
```

## Testing Strategy

### Unit Tests

1. **Transformation Tests**: Verify each transformation rule
   ```csharp
   [Fact]
   public void TransformLiteral_ProducesCallK()
   {
       var transformer = new CpsTransformer();
       var k = Symbol.Intern("k");
       var result = transformer.Transform(42d, k);
       
       Assert.IsType<Cons>(result);
       Assert.Equal(JsSymbols.CallK, result.Head);
   }
   ```

2. **Control Flow Tests**: Verify loops, conditionals
3. **Function Tests**: Verify closure capture and continuation passing
4. **Generator Tests**: Verify yield and resumption
5. **Async Tests**: Verify await and promise integration

### Integration Tests

1. **End-to-End Generator Tests**:
   ```csharp
   [Fact]
   public void Generator_YieldsSequentialValues()
   {
       var engine = new JsEngine();
       engine.Evaluate(@"
           function* gen() {
               yield 1;
               yield 2;
               yield 3;
           }
           let g = gen();
       ");
       
       var r1 = engine.Evaluate("g.next();");
       var r2 = engine.Evaluate("g.next();");
       var r3 = engine.Evaluate("g.next();");
       
       // Verify values
   }
   ```

2. **End-to-End Async Tests**: With mock promises
3. **Complex Scenarios**: Nested generators, async in loops, etc.

### Performance Tests

1. Benchmark synchronous code (should have minimal overhead)
2. Benchmark generator performance
3. Benchmark async/await performance
4. Memory profiling for continuation capture

## Performance Considerations

### Optimization Strategies

1. **Lazy Transformation**: Only transform when necessary
   ```csharp
   if (!ContainsAsyncOrYield(function))
   {
       return function; // Skip transformation
   }
   ```

2. **Continuation Inlining**: For simple continuations
   ```scheme
   ; Instead of: (call-k k value)
   ; Inline if k is simple: value
   ```

3. **Tail Call Optimization**: Essential for CPS
   - All CPS calls are tail calls
   - Evaluator should handle them efficiently

4. **Caching**: Cache transformed functions
   ```csharp
   private Dictionary<Cons, Cons> _transformCache;
   ```

### Expected Overhead

- **Synchronous code**: ~0-5% (no transformation)
- **Generators**: ~20-40% (state machine overhead)
- **Async/Await**: ~15-30% (promise overhead + CPS)

These are acceptable tradeoffs for the functionality gained.

## Migration Path

### Backward Compatibility

1. **No Breaking Changes**: Existing code continues to work
2. **Opt-In Features**: async/await and generators are new syntax
3. **Progressive Enhancement**: Can add features incrementally

### Rollout Plan

1. **Phase 1**: Release CPS infrastructure (internal only)
2. **Phase 2**: Release generators (beta)
3. **Phase 3**: Release async/await (beta)
4. **Phase 4**: Promote to stable after testing
5. **Phase 5**: Optimize based on real-world usage

### Documentation Updates

1. Update README.md to list async/await as supported
2. Add examples for generators
3. Add examples for async/await
4. Document limitations and known issues
5. Add performance guide

## Conclusion

This plan provides a comprehensive roadmap for implementing CPS transformation in Asynkron.JsEngine. By transforming S-expressions after parsing but before evaluation, we can add support for generators and async/await while maintaining:

1. **Backward compatibility**: Existing code works unchanged
2. **Performance**: Only transform when necessary
3. **Clarity**: Separation of concerns between parsing, transformation, and evaluation
4. **Testability**: Each phase can be tested independently

The implementation should proceed in phases, with thorough testing at each stage. The estimated timeline is 12 weeks for full implementation, though individual phases can be released as they're completed.

### Next Steps

1. Get team review and approval of this plan
2. Set up project tracking (issues, milestones)
3. Begin Phase 1 implementation
4. Schedule regular review meetings
5. Update this document based on lessons learned

---

**Document Version**: 1.0  
**Last Updated**: 2025-11-08  
**Author**: GitHub Copilot  
**Status**: Proposed
