# C# Transpiler for JsEngine - Design Notes

## Overview

Theoretical exploration of using the existing `IrBuilder` infrastructure to transpile JavaScript to C# code, which could then be compiled into a `JsHostFunction`. The generated C# would still operate on `JsValue`, `JsObject`, and other JS runtime types.

## Current IR System

The `GeneratorPlan` with its flat `ImmutableArray<GeneratorInstruction>` is well-suited for code generation:

- **Entry point**: `GeneratorIrBuilder.cs` dispatches to `SyncGeneratorIrBuilder`
- **Output**: `GeneratorPlan` - flat instruction array with explicit jumps
- **20+ instruction types**: Branch, Jump, Expression, Return, Yield, Try/Catch, etc.
- **Slot-based storage**: Generator variables stored in indexed slots for O(1) access

## Basic Approach

```csharp
// Generated C# for a simple function
static JsValue GeneratedFunc(JsValue thisArg, JsValue[] args, JsEnvironment env, EvaluationContext ctx)
{
    // Slots become local variables
    var slot0 = JsValue.Undefined;
    var slot1 = JsValue.Undefined;

    L0: // ExpressionInstruction
    slot0 = JsOps.Add(args[0], JsValue.FromNumber(1));
    goto L1;

    L1: // BranchInstruction
    if (JsOps.GreaterThan(slot0, JsValue.FromNumber(10)).IsTruthy) goto L3;
    goto L5;

    L3: // ReturnInstruction
    return slot0;

    L5: // ...
}
```

## Instruction Mapping

| IR Instruction | C# Equivalent |
|----------------|---------------|
| `BranchInstruction` | `if (...) goto Ln; goto Lm;` |
| `JumpInstruction` | `goto Ln;` |
| `ReturnInstruction` | `return expr;` |
| `ExpressionInstruction` | Direct expression transpilation |
| `EnterTryInstruction` | `try {` |
| `ThrowInstruction` | `throw new JsException(...)` |
| `BreakInstruction` | `goto Ln;` (to loop exit) |
| `ContinueInstruction` | `goto Ln;` (to loop header) |

## Implementation Challenges

### 1. Expression Transpilation (Missing Component)

The IR contains `ExpressionNode` references, not transpiled expressions. Need an `ExpressionTranspiler`:

```csharp
// BinaryExpression("+", a, b) →
JsOps.Add(TranspileExpr(a), TranspileExpr(b))

// MemberExpression(obj, "foo") →
JsOps.GetProperty(TranspileExpr(obj), "foo")

// CallExpression(fn, args) →
JsOps.Call(TranspileExpr(fn), new[] { ... })
```

### 2. Generators (yield/resume)

C# doesn't have resumable methods, but can use state machines:

```csharp
// Option A: State machine (like C# iterators)
class GeneratedGenerator : IJsGenerator
{
    private int _state = 0;
    private JsValue _slot0, _slot1;

    public IteratorResult Next(JsValue input)
    {
        switch (_state)
        {
            case 0: goto L0;
            case 1: goto L_Resume1;
            // ...
        }

        L0:
        _slot0 = /* init */;

        L_Yield1:
        _state = 1;
        return new IteratorResult(_slot0, done: false);

        L_Resume1:
        _slot0 = input; // resumed value
        goto L2;
    }
}
```

### 3. Closures - Environment Capture

```csharp
// Inner functions need to capture the JsEnvironment
var innerFunc = new JsHostFunction((thisArg, args, ctx) => {
    // Access outer slot via captured environment
    var outerX = capturedEnv.GetSlot(0);
    // ...
});
```

### 4. Runtime Compilation

**Option A: Roslyn**

```csharp
var syntaxTree = CSharpSyntaxTree.ParseText(generatedCode);
var compilation = CSharpCompilation.Create("DynamicJs")
    .AddReferences(/* Asynkron.JsEngine, mscorlib, etc. */)
    .AddSyntaxTrees(syntaxTree);

using var ms = new MemoryStream();
compilation.Emit(ms);
var assembly = Assembly.Load(ms.ToArray());
var method = assembly.GetType("Generated").GetMethod("Func");
return (JsHostFunction)Delegate.CreateDelegate(typeof(JsHostFunction), method);
```

**Option B: System.Reflection.Emit (faster)**

```csharp
var dm = new DynamicMethod("GeneratedFunc", typeof(JsValue),
    new[] { typeof(JsValue), typeof(JsValue[]), typeof(JsEnvironment) });
var il = dm.GetILGenerator();
// Emit IL directly...
```

## Semantic Differences to Abstract

| JS Semantics | C# Approach |
|--------------|-------------|
| `typeof null === "object"` | `JsOps.TypeOf(val)` returns string |
| `==` vs `===` | `JsOps.LooseEquals()` vs `JsOps.StrictEquals()` |
| `+` concatenation | `JsOps.Add()` handles type coercion |
| Property access | `JsOps.GetProperty()` with prototype chain |
| `this` binding | Explicitly pass `thisArg` |
| `arguments` object | Build from `args[]` |

## Proposed Architecture

```
                    ┌─────────────────┐
                    │  FunctionExpr   │
                    └────────┬────────┘
                             │
                    ┌────────▼────────┐
                    │  IrBuilder      │ (existing)
                    └────────┬────────┘
                             │
                    ┌────────▼────────┐
                    │ GeneratorPlan   │
                    └────────┬────────┘
                             │
              ┌──────────────┼──────────────┐
              │              │              │
     ┌────────▼────────┐     │     ┌────────▼────────┐
     │ IR Interpreter  │     │     │ CSharpEmitter   │ (new)
     │   (existing)    │     │     └────────┬────────┘
     └─────────────────┘     │              │
                             │     ┌────────▼────────┐
                             │     │ Roslyn Compile  │
                             │     └────────┬────────┘
                             │              │
                             │     ┌────────▼────────┐
                             │     │ JsHostFunction  │
                             │     └─────────────────┘
```

## Where This Makes Sense

**Good candidates:**
1. **Hot functions** detected via call count - only compile after N invocations (JIT tiering)
2. **Simple loops** - the for-loop benchmark would benefit enormously
3. **Pure computation** - fibonacci, math-heavy code

**Not worth it for:**
- One-shot initialization code
- Heavily dynamic code with `eval`
- Code with complex closures

## Quick Win: Expression-level Compilation

Even without full function compilation, hot expressions could be compiled:

```csharp
// Instead of EvaluateExpression(node, env, ctx) for every call
// Generate: Func<JsEnvironment, JsValue>
var compiled = CompileExpression(binaryExpr);
// Then invoke: compiled(env)
```

This would eliminate interpreter dispatch for innermost hot loops.

## Implementation Phases

### Phase 1: Simple Functions
- Non-generator, non-async functions
- Basic expressions (arithmetic, comparison, property access)
- Simple control flow (if/else, while, for)
- Local variables only (no closures)

### Phase 2: Full Expression Support
- All expression types
- Function calls
- Object/array literals
- Spread operators

### Phase 3: Closures
- Captured environment handling
- Inner function generation

### Phase 4: Generators
- State machine generation
- yield/yield* support
- Iterator protocol

### Phase 5: Async/Await
- Promise integration
- async generator support

## Key Files to Create

| File | Purpose |
|------|---------|
| `Transpiler/ExpressionTranspiler.cs` | Convert ExpressionNode to C# code |
| `Transpiler/InstructionEmitter.cs` | Map IR instructions to C# |
| `Transpiler/GeneratorStateEmitter.cs` | Handle yield/resume state machines |
| `Transpiler/CSharpCompiler.cs` | Roslyn compilation wrapper |
| `Transpiler/JitTieringPolicy.cs` | Decide when to compile (call count threshold) |

## Conclusion

The existing IR infrastructure provides a solid foundation. The main work is:

1. `ExpressionTranspiler` - translate ExpressionNode to C# code strings
2. `InstructionEmitter` - map IR instructions to C# control flow
3. `GeneratorStateEmitter` - handle yield/resume via state machine
4. Compilation harness (Roslyn or Emit)

This approach could significantly improve performance for hot code paths while maintaining full JavaScript semantics through the `JsOps` abstraction layer.
