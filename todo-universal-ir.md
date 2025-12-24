# Universal IR Execution Analysis

This document analyzes what would be required to use the IR (Intermediate Representation) generator for all JavaScript functions, not just generators and async functions.

## Current State

### Using Native IR Instructions

These constructs are compiled to native IR opcodes and execute without AST fallback:

- **Control flow**: `if`, `while`, `do-while`, `for`, `switch`
- **Iteration**: `for-of`, `for-in`, `for await...of` (with `IteratorInitInstruction`, `IteratorMoveNextInstruction`)
- **Exception handling**: `try/catch/finally` (with `EnterTryInstruction`, `LeaveTryInstruction`)
- **Generator-specific**: `yield`, `yield*`, `return`, `break`, `continue`
- **Scope**: `with` (when body contains yield)

### Now Using Native IR Instructions

These were recently ported to native IR opcodes:

| Construct | Instruction | Notes |
|-----------|-------------|-------|
| Throw statements | `ThrowInstruction` | Evaluates expression and throws |
| Expression statements | `EvaluateAndDiscardInstruction` | Evaluates expression, discards result |
| Function declarations | `FunctionDeclarationInstruction` | No-op (functions are hoisted) |
| Simple variable declarations | `SimpleVariableDeclarationInstruction` | Single identifier binding (let/const/var) |

### Wrapped in StatementInstruction (AST Fallback)

These still execute via AST walking inside the IR execution loop:

| Construct | Reason |
|-----------|--------|
| Class declarations | No IR instruction defined |
| Variable declarations (complex) | Destructuring patterns, multiple declarators |
| With statements | Uses AST when body has no yield |
| Simple for-of | Uses AST when no yield in body/iterable |

### Hard Failures (IR Cannot Handle)

These patterns cause the IR builder to fail entirely, falling back to full AST interpretation:

| Pattern | Example | Reason |
|---------|---------|--------|
| Yield in loop condition | `while(yield x)` | Cannot extract yield from condition |
| Yield in for increment | `for(;; yield x)` | Cannot extract yield from increment |
| Yield in switch discriminant | `switch(yield x)` | Cannot extract yield |
| Yield in case tests | `case yield x:` | Cannot extract yield |
| Yield in if condition | `if(yield x)` | Cannot extract yield |
| Yield in return expression | `return yield x` | Nested yield not supported |
| Yield in throw expression | `throw yield x` | Nested yield not supported |
| Class with yield in computed props | `class { [yield x]() {} }` | Cannot extract from class definition |

## Requirements for Universal IR

### 1. Expression Compilation to IR

Currently all expressions use `EvaluateExpression()` on AST nodes. Would need IR opcodes for:

- [ ] Binary operations (`+`, `-`, `*`, `/`, `%`, `**`, etc.)
- [ ] Unary operations (`!`, `~`, `-`, `+`, `typeof`, `void`, `delete`)
- [ ] Comparison operations (`==`, `===`, `!=`, `!==`, `<`, `>`, `<=`, `>=`)
- [ ] Logical operations (`&&`, `||`, `??`)
- [ ] Property access (`.` and `[]`)
- [ ] Function/method calls
- [ ] Object literals
- [ ] Array literals
- [ ] Template literals
- [ ] Assignment expressions
- [ ] Conditional expressions (`?:`)
- [ ] `new` expressions
- [ ] `this`, `super`
- [ ] Arrow functions
- [ ] Class expressions
- [ ] Spread operator

### 2. Remove StatementInstruction Fallbacks

Convert all statements to native IR instructions:

- [x] `SimpleVariableDeclarationInstruction` - handle let/const/var declarations (simple identifier bindings)
- [x] `EvaluateAndDiscardInstruction` - execute expression and discard result
- [x] `FunctionDeclarationInstruction` - hoist and define function (no-op at runtime)
- [x] `ThrowInstruction` - throw exception
- [ ] `ClassDeclarationInstruction` - define class
- [ ] Complex variable declarations (destructuring, multiple declarators)

### 3. Handle Remaining Yield Positions

For generators, would need yield extraction for more contexts:

- [ ] Yield in loop conditions (requires loop restructuring)
- [ ] Yield in switch discriminant (requires discriminant pre-evaluation)
- [ ] Yield in if conditions (requires condition pre-evaluation)
- [ ] Nested yields (requires flattening)

### 4. Optimization Opportunities

If pursuing full IR execution, consider:

- [ ] Register allocation for local variables
- [ ] Constant folding
- [ ] Dead code elimination
- [ ] Inline caching for property access
- [ ] Type specialization for hot paths

## Current Architecture Value

The main value of the current IR architecture is **enabling suspension/resumption for generators and async functions**. For regular (non-generator, non-async) functions:

- No suspension/resumption needed
- Control flow can be handled directly by C# control flow
- AST walking is already efficient
- `StatementInstruction` wrapper allows hybrid execution

## Recommendation

The current hybrid approach (native IR for control flow + AST fallback for expressions/simple statements) is pragmatic. Full IR compilation would be significant work with unclear performance benefits since:

1. Expression evaluation is already optimized with fast paths
2. The hot paths (loops, property access) already have specialized handling
3. Jint (a comparable engine) also uses AST walking and achieves good performance

Consider full IR only if profiling shows significant overhead from AST node dispatch in hot loops.
