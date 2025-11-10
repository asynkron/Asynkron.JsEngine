# Signal Pattern vs State Machine: Analysis & Conclusion

## Original Question
> Would it be easier to manage signals such as break or return if we replaced those with typed results?
>
> ```csharp
> public interface ISignal { whateverweneed };
> public record ReturnSignal(valueToReturn) : ISignal;
> public record YieldSignal(valueToYield): ISignal;
> public record BreakSignal(): ISignal;
> 
> if (result is ISignal signal) {
>      // exit evaluation block, or whatever we need
> }
> ```
>
> is that the same, better or worse than the statemachine we have?

## Answer: Better - and Now Implemented

The typed signal approach is **significantly better** than the enum-based state machine, and this PR implements it. Here's why:

## Comparison

### State Machine (Old Approach)
```csharp
// Setting state
context.Flow = ControlFlow.Return;
context.FlowValue = value;

// Checking state - requires two separate properties
if (context.Flow == ControlFlow.Return)
{
    var value = context.FlowValue;
}

// Easy to make mistakes
context.Flow = ControlFlow.Return;
// Forgot to set FlowValue!
```

### Typed Signals (New Approach)
```csharp
// Setting state - single cohesive object
context.CurrentSignal = new ReturnSignal(value);

// Checking state - type-safe pattern matching
if (context.CurrentSignal is ReturnSignal signal)
{
    var value = signal.Value;
}

// Can't forget the value - it's required by the constructor
new ReturnSignal(value);  // Compiler enforces completeness
```

## Key Advantages

### 1. Type Safety
- **Before**: Enum values are just integers - easy to mix up
- **After**: Each signal type is distinct - compiler catches mistakes

```csharp
// With signals, this won't compile:
if (context.CurrentSignal is ReturnSignal)
{
    // Can't accidentally treat it as a BreakSignal
}
```

### 2. Modern C# Features
Enables powerful pattern matching:

```csharp
// Switch expression with deconstruction
var message = context.CurrentSignal switch
{
    ReturnSignal { Value: int n } => $"Returning int: {n}",
    ReturnSignal { Value: string s } => $"Returning string: {s}",
    BreakSignal => "Breaking",
    _ => "Other signal"
};
```

### 3. Cohesion
- **Before**: State and value are separate fields
- **After**: Signal and value are a single object

```csharp
// Before: Two separate fields that must stay in sync
context.Flow = ControlFlow.Return;
context.FlowValue = returnValue;

// After: Single atomic unit
context.CurrentSignal = new ReturnSignal(returnValue);
```

### 4. Extensibility
Easy to add new signal types:

```csharp
// Just define a new record - no enum modification needed
internal sealed record AwaitSignal(object? Promise) : ISignal;

// Existing code continues to work
```

### 5. Self-Documenting
The code is more explicit about intent:

```csharp
// Before: What does "Flow = Return" mean?
context.Flow = ControlFlow.Return;

// After: Crystal clear
context.CurrentSignal = new ReturnSignal(value);
```

## Implementation Approach

This PR implements a **hybrid approach** for smooth migration:

1. **Internal Implementation**: Uses signals internally
2. **Backward Compatibility**: Old API still works (marked obsolete)
3. **Gradual Migration**: New code can use pattern matching
4. **No Breaking Changes**: Existing code continues to function

```csharp
// Old code still works
context.SetReturn(value);
if (context.IsReturn) { ... }

// New code can use signals
if (context.CurrentSignal is ReturnSignal signal) { ... }
```

## Demonstration

See `EvaluateWhile` in `Evaluator.cs` for a real example:

```csharp
switch (context.CurrentSignal)
{
    case ContinueSignal:
        context.ClearContinue();
        continue;
    
    case BreakSignal:
        context.ClearBreak();
        break;
    
    case ReturnSignal or ThrowFlowSignal:
        break;  // Propagate up
}
```

This is much clearer than the previous enum-based checks.

## Test Results

- **1078 total tests** (added 5 new)
- **1064 passing** (up from 1059)
- **14 failing** (same pre-existing failures)
- **0 CodeQL security alerts**

## Conclusion

The typed signal approach is **definitively better** than the state machine:

✅ More type-safe  
✅ More maintainable  
✅ More extensible  
✅ More idiomatic (modern C#)  
✅ Backward compatible  
✅ Better developer experience

## Documentation

See [SIGNAL_PATTERN.md](SIGNAL_PATTERN.md) for detailed examples and migration guide.
