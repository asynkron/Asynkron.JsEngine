# State Machine Approach Clarification

> **Legacy note (2025):** This note discusses control-flow patterns for the original S-expression evaluator. The current
> engine uses a typed AST (`TypedAstEvaluator`) plus generator IR to model control flow, but the context-based control
> flow ideas here still apply conceptually.

## The Confusion

In `CONTROL_FLOW_ALTERNATIVES.md`, I described an approach called "State Machine with Control Flags". This name can be misleading because it suggests transforming S-expressions into state machines, but that's **NOT** what this approach does.

## What It Actually Means

### It's About Runtime State, Not S-Expression Transformation

The "State Machine" approach is really about:

1. **No S-expression transformation** - The S-expressions stay exactly the same
2. **Runtime state tracking** - Pass a context object through the evaluator
3. **Control flow flags** - Track "we're in a return" state vs "we're in a break" state

A better name would be: **"Context-Based Control Flow"** or **"Control Flow Flags Pattern"**

## The Three Categories of Approaches

Let me clarify the three fundamentally different categories:

### Category A: Transform S-Expressions (at Parse/Transform Time)

**CPS Transformation:**
```
Original S-expr: (return x)
Transformed:     (call return-continuation x)
```

This changes the **structure** of the S-expression tree before evaluation.

### Category B: Change What Evaluator Returns (at Runtime)

**Result Wrapper:**
```csharp
// Every evaluation returns a wrapped result
EvalResult { Kind = ResultKind.Return, Value = 42 }
```

This changes the **return type** of every evaluator method.

### Category C: Pass State Object (at Runtime)

**"State Machine" / Control Flow Context:**
```csharp
// Evaluation methods take a context parameter
EvaluateStatement(statement, environment, context);

// Context tracks current control flow state
context.Flow = ControlFlow.Return;  // Set flag
if (context.Flow == ControlFlow.Return) { ... }  // Check flag
```

This **adds a parameter** but keeps S-expressions and return types the same.

## How "State Machine" Actually Works

### The State Being Tracked

The context object tracks the **execution state** of the evaluator:

```csharp
internal sealed class EvaluationContext
{
    // States the execution can be in
    public enum ControlFlow
    {
        None,      // Normal execution (initial state)
        Return,    // We hit a return statement
        Break,     // We hit a break statement
        Continue,  // We hit a continue statement
        Throw      // We hit a throw statement
    }
    
    public ControlFlow Flow { get; private set; } = ControlFlow.None;
    public object? FlowValue { get; private set; }
}
```

### State Transitions

The evaluator changes the state when encountering control flow:

```csharp
// State transition: None → Return
if (ReferenceEquals(symbol, JsSymbols.Return))
{
    var value = EvaluateExpression(cons.Rest.Head, environment, context);
    context.Flow = ControlFlow.Return;  // ← State transition
    context.FlowValue = value;
    return value;
}

// State transition: None → Break
if (ReferenceEquals(symbol, JsSymbols.Break))
{
    context.Flow = ControlFlow.Break;  // ← State transition
    return null;
}
```

### State Checking

Every evaluation checks the state and acts accordingly:

```csharp
private static object? EvaluateBlock(Cons block, Environment environment, 
                                     EvaluationContext context)
{
    var scope = new Environment(environment);
    object? result = null;
    
    foreach (var statement in block.Rest)
    {
        result = EvaluateStatement(statement, scope, context);
        
        // Check state: if not None, stop evaluating
        if (context.Flow != ControlFlow.None)
            break;  // Stop processing remaining statements
    }
    
    return result;
}
```

### State Consumption

Loops consume Break/Continue states:

```csharp
private static object? EvaluateWhile(Cons cons, Environment environment,
                                     EvaluationContext context)
{
    // ... loop setup ...
    
    while (true)
    {
        // ... condition check ...
        
        lastResult = EvaluateStatement(body, environment, context);
        
        // Check and consume Continue state
        if (context.Flow == ControlFlow.Continue)
        {
            context.Flow = ControlFlow.None;  // ← Reset state (consume)
            continue;  // Next iteration
        }
        
        // Check and consume Break state
        if (context.Flow == ControlFlow.Break)
        {
            context.Flow = ControlFlow.None;  // ← Reset state (consume)
            break;  // Exit loop
        }
        
        // Propagate Return/Throw states (don't consume)
        if (context.Flow == ControlFlow.Return || context.Flow == ControlFlow.Throw)
            break;  // Exit loop but keep state
    }
    
    return lastResult;
}
```

Functions consume Return states:

```csharp
public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
{
    var context = new EvaluationContext();  // Fresh context per function
    var environment = new Environment(_closure, isFunctionScope: true);
    
    // ... parameter binding ...
    
    var result = Evaluator.EvaluateBlock(_body, environment, context);
    
    // Check and consume Return state
    if (context.Flow == ControlFlow.Return)
        return context.FlowValue;  // Return the return value
    
    return result;  // Normal completion
}
```

## Why "State Machine"?

The term comes from the fact that the context object behaves like a **finite state machine**:

### States:
- None (initial state)
- Return
- Break
- Continue
- Throw

### Transitions:
- Encountering `return` → transition to Return state
- Encountering `break` → transition to Break state
- Encountering `continue` → transition to Continue state
- Loop consuming break → transition back to None state
- Function consuming return → (context destroyed)

### State Diagram:

```
           [None]
             |
      eval(return x)
             |
             v
         [Return] -----> function boundary: consume, extract value
             |
             x (propagate up)
             
           [None]
             |
       eval(break)
             |
             v
          [Break] -----> loop boundary: consume, exit loop
             |
             x (propagate up if no loop)
             
           [None]
             |
      eval(continue)
             |
             v
        [Continue] ----> loop boundary: consume, next iteration
             |
             x (propagate up if no loop)
```

## Contrast with Actual State Machine Transformation

An **actual state machine transformation** would look completely different. It would transform the S-expressions themselves:

### Example: State Machine Transformation (NOT what we're doing)

```csharp
// Original JavaScript:
function example() {
    let x = 1;
    if (x > 0) {
        return x;
    }
    x = x + 1;
    return x;
}

// Original S-expression:
(function example ()
  (block
    (let x 1)
    (if (> x 0)
        (return x))
    (assign x (+ x 1))
    (return x)))

// Hypothetical State Machine Transformation (we DON'T do this):
(function example ()
  (block
    (let __state 0)
    (let x null)
    (let __result null)
    (while true
      (switch __state
        (case 0
          (assign x 1)
          (assign __state 1))
        (case 1
          (if (> x 0)
              (block
                (assign __result x)
                (assign __state 99))  ; 99 = exit state
              (assign __state 2)))
        (case 2
          (assign x (+ x 1))
          (assign __state 3))
        (case 3
          (assign __result x)
          (assign __state 99))
        (case 99
          (return __result))))))
```

This transforms the **structure** of the code into an explicit state machine with numbered states.

### We DON'T Do This

The "State Machine with Control Flags" approach in the document does **NOT** do this transformation. It:

1. **Keeps S-expressions unchanged**
2. **Adds a context parameter to evaluator methods**
3. **Tracks control flow state at runtime**

## Why Not Actually Transform to State Machines?

Transforming to actual state machines would be similar to CPS:

### Pros of Actual State Machine Transformation:
✅ Very explicit control flow
✅ Can serialize and resume execution
✅ Good for debuggers

### Cons of Actual State Machine Transformation:
❌ Complex transformation logic
❌ Makes S-expressions much larger
❌ Harder to debug (code becomes unrecognizable)
❌ Similar complexity to CPS transformation
❌ Overkill for simple control flow

## Summary: What "State Machine" Really Means

| Aspect | What It Is | What It's NOT |
|--------|-----------|---------------|
| **S-expressions** | Unchanged | NOT transformed into state machines |
| **Transformation** | None | NOT a compile-time transformation |
| **Evaluator** | Takes extra context parameter | NOT a different evaluation strategy |
| **Control flow** | Tracked via runtime flags | NOT explicit state numbers in code |
| **Complexity** | Low (just add parameter) | NOT high like CPS |

## Better Name Suggestions

To avoid confusion, here are better names for this approach:

1. **"Control Flow Context Pattern"** - Most accurate
2. **"Evaluation Context with Flags"** - Descriptive
3. **"Runtime Control Flow Tracking"** - Clear about timing
4. **"Context-Passing Style"** (not to be confused with Continuation-Passing Style)

## Comparison Table: The Three Runtime Approaches

| Aspect | Exceptions | Result Wrapper | Control Flow Context |
|--------|-----------|----------------|---------------------|
| **S-expressions** | Unchanged | Unchanged | Unchanged |
| **Return type** | object? | EvalResult | object? |
| **Extra parameter** | No | No | Yes (context) |
| **How signal travels** | Stack unwinding | Return value | Context mutation |
| **Complexity** | Low | Medium | Low |

## The Actual Best Name

Given this clarification, the approach should probably be called:

**"Mutable Evaluation Context Pattern"**

This makes it clear that:
- It's about context (not transformation)
- The context is mutable (state changes)
- It happens during evaluation (runtime)

---

## Conclusion

The "State Machine with Control Flags" is **NOT** about transforming S-expressions. It's about:

1. Creating a context object to track execution state
2. Passing this context through evaluator methods
3. Setting flags when control flow is encountered
4. Checking flags to decide whether to continue or stop
5. Clearing flags when they're consumed (loops, functions)

It's called "State Machine" because the context object acts like a finite state machine with states (None, Return, Break, Continue, Throw) and transitions between them.

**Better names:** "Control Flow Context Pattern" or "Mutable Evaluation Context Pattern"

---

**Document Version**: 1.0  
**Last Updated**: 2025-01-08  
**Author**: GitHub Copilot Workspace  
**Status**: Clarification of CONTROL_FLOW_ALTERNATIVES.md
