# Architecture Overview

This document describes the internal architecture and design decisions of Asynkron.JsEngine.

## Table of Contents

- [High-Level Architecture](#high-level-architecture)
- [Core Components](#core-components)
- [Execution Pipeline](#execution-pipeline)
- [S-Expression Representation](#s-expression-representation)
- [Environment and Scoping](#environment-and-scoping)
- [Type System](#type-system)
- [Asynchronous Execution](#asynchronous-execution)
- [Module System](#module-system)
- [Design Decisions](#design-decisions)
- [Performance Considerations](#performance-considerations)

---

## High-Level Architecture

Asynkron.JsEngine is a **tree-walking interpreter** that uses an **S-expression intermediate representation** inspired by Lisp. The engine transforms JavaScript code through multiple stages before execution.

### Architecture Diagram

```
┌──────────────────┐
│  JavaScript      │
│  Source Code     │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│   Lexer          │  Tokenize source into tokens
│   (Tokenizer)    │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│   Parser         │  Build S-expression tree
│                  │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ CPS Transformer  │  Transform async constructs
│                  │  (for async/await support)
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  S-Expression    │
│  Tree            │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  Evaluator       │  Walk tree and execute
│  (Interpreter)   │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  Result          │
└──────────────────┘
```

---

## Core Components

### 1. Lexer (Tokenizer)

**Location:** `Lexer.cs`

**Purpose:** Converts JavaScript source code into a stream of tokens.

**Key Features:**
- Handles single-line (`//`) and multi-line (`/* */`) comments
- Recognizes all JavaScript operators and keywords
- Supports string literals (single and double quotes)
- Handles template literals with interpolation
- Supports regex literals (`/pattern/flags`)
- Tracks source position for error reporting

**Example:**
```javascript
let x = 10;
```
Produces tokens:
```
[Let] [Identifier("x")] [Assign] [Number(10)] [Semicolon]
```

### 2. Parser

**Location:** `Parser.cs`

**Purpose:** Converts token stream into an S-expression tree.

**Key Features:**
- Recursive descent parsing
- Operator precedence handling
- Statement and expression parsing
- Error recovery and reporting
- Source reference tracking

**Example:**
```javascript
let x = 2 + 3;
```
Produces S-expression:
```lisp
(let x (+ 2 3))
```

### 3. CPS Transformer

**Location:** `CpsTransformer.cs`

**Purpose:** Transforms async/await constructs into Continuation-Passing Style (CPS).

**Key Features:**
- Converts `async` functions to Promise-returning functions
- Transforms `await` expressions into Promise chains
- Preserves error handling with try/catch
- Maintains control flow semantics

**See:** [TRANSFORMATIONS.md](TRANSFORMATIONS.md) for detailed examples.

### 4. Evaluator

**Location:** `Evaluator.cs`

**Purpose:** Executes the S-expression tree.

**Key Features:**
- Tree-walking interpreter
- Environment-based scoping
- Closure support
- Exception handling
- Control flow signals (return, break, continue, throw)

### 5. Environment

**Location:** `Environment.cs`

**Purpose:** Manages variable bindings and scopes.

**Key Features:**
- Lexical scoping with parent chain
- Function scope vs block scope distinction
- Strict mode support
- Variable shadowing
- Closure capture

---

## Execution Pipeline

### Step 1: Lexing

```csharp
string source = "let x = 10;";
var lexer = new Lexer(source);
var tokens = lexer.Tokenize();
```

### Step 2: Parsing

```csharp
var parser = new Parser(tokens, source);
var sexpr = parser.ParseProgram();
// Result: (program (let x 10))
```

### Step 3: Transformation (Optional)

```csharp
var transformer = new CpsTransformer();
var transformed = transformer.Transform(sexpr);
// Transforms async/await into CPS
```

### Step 4: Evaluation

```csharp
var environment = new Environment(isFunctionScope: true);
var result = Evaluator.EvaluateProgram(transformed, environment);
```

---

## S-Expression Representation

The engine uses **S-expressions** (symbolic expressions) as its intermediate representation, similar to Lisp.

### Cons Cells

The fundamental building block is the `Cons` class, representing a cons cell (CAR/CDR pair).

```csharp
public sealed class Cons
{
    public object? Head { get; }  // CAR
    public Cons Rest { get; }     // CDR
    public bool IsEmpty { get; }
}
```

### S-Expression Examples

**Variable Declaration:**
```javascript
let x = 10;
```
```lisp
(let x 10)
```

**Function Call:**
```javascript
add(2, 3)
```
```lisp
(call add 2 3)
```

**Binary Operation:**
```javascript
a + b * c
```
```lisp
(+ a (* b c))
```

**If Statement:**
```javascript
if (x > 5) {
    console.log("big");
} else {
    console.log("small");
}
```
```lisp
(if (> x 5)
    (block (expr-stmt (call (get-prop console "log") "big")))
    (block (expr-stmt (call (get-prop console "log") "small"))))
```

**Function Declaration:**
```javascript
function add(a, b) {
    return a + b;
}
```
```lisp
(function add (a b) (block (return (+ a b))))
```

### Symbol Interning

Symbols (like `let`, `function`, `if`) are **interned** for efficient comparison:

```csharp
public static class JsSymbols
{
    public static readonly Symbol Let = Symbol.Intern("let");
    public static readonly Symbol Function = Symbol.Intern("function");
    // ... etc
}
```

This allows using reference equality (`ReferenceEquals`) instead of string comparison:

```csharp
if (ReferenceEquals(symbol, JsSymbols.Let))
{
    // Handle let declaration
}
```

---

## Environment and Scoping

### Environment Chain

Each scope has an `Environment` object that chains to its parent:

```
Global Environment
    └─> Function Scope (outer)
            └─> Block Scope
                    └─> Function Scope (inner)
```

### Variable Lookup

1. Search current environment
2. If not found, search parent
3. Continue up the chain
4. If not found at global level: `ReferenceError`

**Example:**
```javascript
let x = 10;          // Global scope

function outer() {
    let y = 20;      // Function scope
    
    {
        let z = 30;  // Block scope
        console.log(x, y, z); // All accessible
    }
    
    // z not accessible here
}
```

### Closures

Closures are implemented by capturing the environment:

```javascript
function makeCounter() {
    let count = 0;  // Captured in closure
    return function() {
        count = count + 1;
        return count;
    };
}
```

The inner function retains a reference to its parent environment, keeping `count` alive.

---

## Type System

### JavaScript Values in C#

| JavaScript | C# Type | Notes |
|-----------|---------|-------|
| `number` | `double` | All numbers are double precision |
| `string` | `string` | Immutable strings |
| `boolean` | `bool` | true/false |
| `null` | `null` | C# null reference |
| `undefined` | `JsSymbols.Undefined` | Special marker symbol |
| `object` | `JsObject` | Dictionary-based |
| `function` | `JsFunction` | Closure-supporting |
| `array` | `JsObject` | Object with numeric indices |
| `symbol` | `Symbol` | Unique identifiers |
| `Promise` | `JsPromise` | Async operation |

### Type Coercion

The engine implements JavaScript's type coercion rules:

**ToPrimitive:**
```csharp
public static object? ToPrimitive(object? value)
{
    if (value is JsObject obj)
    {
        // Call toString() or valueOf()
    }
    return value;
}
```

**ToNumber:**
```csharp
public static double ToNumber(object? value)
{
    if (value == null) return 0;
    if (value is bool b) return b ? 1 : 0;
    if (value is string s) return ParseNumber(s);
    // ... etc
}
```

**ToBoolean:**
```csharp
public static bool ToBoolean(object? value)
{
    // Falsy: false, 0, "", null, undefined, NaN
    // Everything else is truthy
}
```

---

## Asynchronous Execution

### Event Queue

The engine maintains an event queue for async operations:

```csharp
private readonly Channel<Func<Task>> _eventQueue;
```

### Promise Implementation

Promises are implemented as `JsPromise` objects:

```csharp
public class JsPromise
{
    public Task<object?> Task { get; }
    // Implements then, catch, finally
}
```

### Async/Await Transformation

Async functions are transformed using CPS:

**Original:**
```javascript
async function fetchData() {
    let result = await Promise.resolve(42);
    return result * 2;
}
```

**Transformed:**
```javascript
function fetchData() {
    return new Promise(function(__resolve, __reject) {
        Promise.resolve(42).then(function(result) {
            __resolve(result * 2);
        })['catch'](function(__error) {
            __reject(__error);
        });
    });
}
```

### setTimeout/setInterval

Timers schedule callbacks in the event queue:

```csharp
engine.SetGlobalFunction("setTimeout", args =>
{
    var callback = args[0];
    var delay = Convert.ToInt32(args[1]);
    
    // Schedule in event queue after delay
    Task.Delay(delay).ContinueWith(_ =>
    {
        _eventQueue.Writer.TryWrite(() => ExecuteCallback(callback));
    });
    
    return timerId;
});
```

---

## Module System

### Module Loading

Modules are loaded through a user-provided function:

```csharp
engine.SetModuleLoader(modulePath =>
{
    return File.ReadAllText(modulePath);
});
```

### Module Caching

Modules are evaluated once and cached:

```csharp
private readonly Dictionary<string, JsObject> _moduleRegistry;

if (_moduleRegistry.TryGetValue(modulePath, out var exports))
{
    return exports; // Use cached
}

// Load, parse, evaluate
var exports = EvaluateModule(modulePath);
_moduleRegistry[modulePath] = exports;
```

### Module Exports

Exports are stored in a `JsObject`:

```javascript
export function add(a, b) { return a + b; }
export const PI = 3.14159;
```

Creates:
```csharp
{
    "add": <JsFunction>,
    "PI": 3.14159
}
```

---

## Design Decisions

### 1. Why S-Expressions?

**Advantages:**
- **Uniform representation**: Everything is a list
- **Easy to transform**: CPS transformation is simpler
- **Homoiconicity**: Code is data (like Lisp)
- **Debugging**: Clear representation of program structure

**Trade-offs:**
- More memory than bytecode
- Slower than compiled bytecode
- But simpler implementation

### 2. Why Tree-Walking Interpreter?

**Advantages:**
- Simpler implementation
- Easier to understand and maintain
- Flexible for experimentation
- Good enough performance for most use cases

**Alternatives considered:**
- **Bytecode VM**: More complex, see [BYTECODE_COMPILATION.md](BYTECODE_COMPILATION.md)
- **JIT compilation**: Much more complex
- **Transpilation to C#**: Loss of dynamic features

### 3. Why CPS for Async?

**Advantages:**
- Transforms async into synchronous code with callbacks
- No need for stack manipulation
- Works with tree-walking interpreter
- Clear semantics

**Alternative:**
- Could use state machines (like C# compiler does)
- See [CONTROL_FLOW_STATE_MACHINE_CLARIFICATION.md](CONTROL_FLOW_STATE_MACHINE_CLARIFICATION.md)

### 4. Why Dictionary-Based Objects?

**Advantages:**
- Dynamic property addition/removal
- Simple implementation
- Matches JavaScript semantics

**Trade-offs:**
- Slower than fixed-layout objects
- More memory overhead
- But necessary for JavaScript semantics

### 5. Symbol Interning

**Why:**
- Fast comparisons (reference equality)
- Reduced memory (one instance per symbol)
- Type safety (Symbol type vs string)

**Implementation:**
```csharp
private static readonly Dictionary<string, Symbol> InternedSymbols = new();

public static Symbol Intern(string name)
{
    if (!InternedSymbols.TryGetValue(name, out var symbol))
    {
        symbol = new Symbol(name);
        InternedSymbols[name] = symbol;
    }
    return symbol;
}
```

---

## Performance Considerations

### 1. Optimization Opportunities

**Current state:**
- Tree-walking interpreter (slowest approach)
- No JIT compilation
- No inline caching
- Dictionary-based property access

**Potential improvements:**
- Bytecode compilation (10-20x faster)
- Inline caching for property access
- Type specialization
- Function inlining

See [BYTECODE_COMPILATION.md](BYTECODE_COMPILATION.md) for bytecode approach.

### 2. Memory Management

**S-Expression overhead:**
- Each Cons cell: ~32 bytes minimum
- Large programs create many objects
- Garbage collected by .NET

**Mitigation:**
- Symbol interning reduces duplication
- Empty list singleton
- Immutable structures allow sharing

### 3. Call Stack

**Limitations:**
- Recursive evaluation can overflow stack
- Deep recursion in JavaScript = deep recursion in C#

**Solutions:**
- Could use trampoline technique
- Could compile to bytecode (no recursion)
- See [ITERATIVE_EVALUATION.md](ITERATIVE_EVALUATION.md)

---

## Code Organization

### Source Structure

```
src/Asynkron.JsEngine/
├── Lexer.cs              # Tokenization
├── Parser.cs             # Parsing to S-expressions
├── CpsTransformer.cs     # Async/await transformation
├── Evaluator.cs          # Tree-walking interpreter
├── Environment.cs        # Scoping and variables
├── Cons.cs               # S-expression representation
├── Symbol.cs             # Interned symbols
├── JsSymbols.cs          # Built-in symbol definitions
├── JsObject.cs           # JavaScript objects
├── JsFunction.cs         # JavaScript functions
├── JsPromise.cs          # Promise implementation
├── HostFunction.cs       # C# interop
├── StandardLibrary.cs    # Built-in objects (Math, Array, etc.)
├── JsEngine.cs           # Main API facade
└── ... (50+ files total)
```

### Key Classes

**JsEngine**
- High-level API
- Coordinates all components
- Manages global environment

**Evaluator**
- Static class with evaluation methods
- Pattern matches on S-expressions
- Returns values or signals

**Environment**
- Variable storage
- Parent chain for scoping
- Strict mode tracking

**Cons**
- S-expression representation
- Immutable list structure
- Source reference tracking

---

## Extension Points

### 1. Adding Built-in Functions

```csharp
engine.SetGlobalFunction("myFunc", args =>
{
    // Implementation
    return result;
});
```

### 2. Custom Module Loader

```csharp
engine.SetModuleLoader(path =>
{
    // Load from database, network, etc.
    return moduleSource;
});
```

### 3. Custom Object Types

Implement custom JavaScript objects:

```csharp
public class MyCustomObject : JsObject
{
    // Custom behavior
}
```

---

## Testing Strategy

### Unit Tests

- Lexer tests: Token recognition
- Parser tests: S-expression correctness
- Evaluator tests: Execution semantics
- Feature tests: JavaScript compatibility

### Integration Tests

- End-to-end JavaScript execution
- Module system tests
- Async/await tests
- NPM package compatibility tests

### Test Location

```
tests/Asynkron.JsEngine.Tests/
├── LexerTests.cs
├── ParserTests.cs
├── EvaluatorTests.cs
├── ModuleTests.cs
├── AsyncTests.cs
└── ... (40+ test files)
```

---

## Future Improvements

### Potential Enhancements

1. **Bytecode Compilation**
   - 10-20x performance improvement
   - See [BYTECODE_COMPILATION.md](BYTECODE_COMPILATION.md)

2. **Inline Caching**
   - Cache property lookups
   - Significant speedup for property access

3. **Type Specialization**
   - Detect numeric-only operations
   - Generate specialized code paths

4. **Tail Call Optimization**
   - Enable deep recursion
   - Match JavaScript spec

5. **Source Maps**
   - Map errors to original source
   - Better debugging experience

---

## Comparison to Other Engines

### V8 (Chrome, Node.js)

- **Compilation**: JIT to native code
- **Performance**: 100-1000x faster
- **Complexity**: Millions of lines of C++
- **Our engine**: Simple, maintainable, good enough for many use cases

### Jint (.NET JS engine)

- **Approach**: Tree-walking + some optimizations
- **Maturity**: More mature, more features
- **Our engine**: Cleaner architecture, S-expressions, easier to understand

### SpiderMonkey (Firefox)

- **Compilation**: Interpreter + JIT
- **Features**: Full ES2023+
- **Our engine**: Subset of features, educational value

---

## References

- **[Transformation Pipeline](TRANSFORMATIONS.md)** - Detailed transformation examples
- **[CPS Transformation](CPS_TRANSFORMATION_PLAN.md)** - Async/await strategy
- **[Bytecode Compilation](BYTECODE_COMPILATION.md)** - Alternative approach
- **[Iterative Evaluation](ITERATIVE_EVALUATION.md)** - Stack-safe evaluation

---

## Summary

Asynkron.JsEngine prioritizes **simplicity and clarity** over raw performance:

✅ **Simple architecture** - Easy to understand and modify
✅ **S-expression IR** - Uniform representation, easy transformations
✅ **Tree-walking interpreter** - Straightforward execution
✅ **CPS transformation** - Clean async/await support
✅ **Good JavaScript compatibility** - 96% feature coverage

This makes it ideal for:
- Embedding in .NET applications
- Educational purposes
- Scripting scenarios
- Applications where simplicity > raw speed
