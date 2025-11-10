# Control Flow Signal Pattern

This document explains the typed signal pattern used for control flow management in the JavaScript engine.

## Overview

Instead of using an enum-based state machine, the engine now uses typed record objects (`ISignal`) to represent control flow states. This provides better type safety and enables pattern matching.

## Signal Types

### ISignal Interface
Base interface for all control flow signals.

### Signal Records
- **`ReturnSignal(object? Value)`** - Represents a `return` statement
- **`BreakSignal()`** - Represents a `break` statement  
- **`ContinueSignal()`** - Represents a `continue` statement
- **`YieldSignal(object? Value)`** - Represents a `yield` expression (generators)
- **`ThrowFlowSignal(object? Value)`** - Represents a `throw` statement

## Usage Examples

### Pattern Matching with Switch Expressions

```csharp
// Check signal type and extract value in one operation
var result = context.CurrentSignal switch
{
    ReturnSignal rs => rs.Value,
    YieldSignal ys => ys.Value,
    ThrowFlowSignal ts => throw new Exception(),
    _ => null
};
```

### Pattern Matching with Switch Statements

```csharp
// Handle different signals with appropriate actions
switch (context.CurrentSignal)
{
    case ContinueSignal:
        context.ClearContinue();
        continue;
    
    case BreakSignal:
        context.ClearBreak();
        break;
    
    case ReturnSignal:
    case ThrowFlowSignal:
        // Propagate return/throw signals up the call stack
        break;
}
```

### Pattern Matching with Is Expression

```csharp
// Simple type check
if (context.CurrentSignal is ReturnSignal)
{
    return context.FlowValue;
}

// Type check with pattern
if (context.CurrentSignal is ReturnSignal or ThrowFlowSignal)
{
    break;  // Exit loop
}

// Type check with deconstruction
if (context.CurrentSignal is ReturnSignal { Value: var returnValue })
{
    Console.WriteLine($"Returning: {returnValue}");
}
```

## Benefits

### 1. Type Safety
Each control flow type is distinct, preventing confusion:
```csharp
// Before: Easy to mix up enum values
if (context.Flow == ControlFlow.Return) { ... }

// After: Compiler enforces type safety
if (context.CurrentSignal is ReturnSignal) { ... }
```

### 2. Clearer Intent
```csharp
// Before: Separate state and value
context.Flow = ControlFlow.Return;
context.FlowValue = value;

// After: Single, cohesive object
context.CurrentSignal = new ReturnSignal(value);
```

### 3. Pattern Matching
Modern C# pattern matching capabilities:
```csharp
// Switch expression with deconstruction
var message = signal switch
{
    ReturnSignal { Value: int n } => $"Returning int: {n}",
    ReturnSignal { Value: string s } => $"Returning string: {s}",
    BreakSignal => "Breaking",
    _ => "Other signal"
};
```

### 4. Extensibility
Easy to add new signal types without modifying enums:
```csharp
// Just add a new record
internal sealed record AwaitSignal(object? Value) : ISignal;
```

## Backward Compatibility

The `EvaluationContext` class maintains backward compatibility:
- Old `Flow` property still works (marked obsolete)
- Old `IsReturn`, `IsBreak`, etc. properties still work
- Internal implementation uses signals

## Migration Path

### Current (Backward Compatible)
```csharp
context.SetReturn(value);
if (context.IsReturn) { ... }
```

### Future (Direct Signal Usage)
```csharp
return new ReturnSignal(value);
if (result is ISignal signal) { ... }
```

## Implementation Details

See `ISignal.cs` for signal type definitions and `EvaluationContext.cs` for the integration with the existing API.
