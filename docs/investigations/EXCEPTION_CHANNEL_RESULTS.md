# Exception Channel Investigation Results

## Summary

Added exception channel to JsEngine to capture unhandled exceptions during JavaScript execution. This allows tests to subscribe and see exceptions that occur during execution.

## Implementation

### New Components

1. **ExceptionInfo class** (`src/Asynkron.JsEngine/ExceptionInfo.cs`)
   - Captures exception details, context, and call stack
   - Similar structure to DebugMessage class
   - Properties: Exception, Context, CallStack, Message, ExceptionType

2. **Exception Channel** (in `JsEngine.cs`)
   - `Channel<ExceptionInfo> _exceptionChannel` - stores exceptions
   - `ChannelReader<ExceptionInfo> Exceptions()` - public method to read exceptions
   - `void LogException(Exception, string context, Environment?)` - internal method to log exceptions

3. **Exception Logging**
   - Added to `StandardLibrary.CreateIteratorNextHelper()` 
   - Logs exceptions that occur when calling `iterator.next()`

### Test Results

**Test G: CaptureExceptionsWithChannel**
```
LOG: Symbol.iterator called
LOG: Caught exception: The empty list does not have a head.

=== EXCEPTIONS CAPTURED: 1 ===
Exception: InvalidOperationException
Message: The empty list does not have a head.
Context: Iterator.next() invocation
Call Stack: (empty - function has no valid call stack)
```

## Key Findings

### Exception Captured ✅

The exception channel successfully captures:
- **Exception Type**: `InvalidOperationException`
- **Message**: "The empty list does not have a head."
- **Context**: "Iterator.next() invocation"
- **Call Stack**: Empty (because the function body is corrupted)

### What This Tells Us

1. **The exception IS being thrown** when `next()` is called
2. **It's caught by the try-catch** in `__iteratorNext` and converted to a rejected promise
3. **The exception occurs in the Cons evaluator** when trying to access `.Head` on an empty Cons
4. **The call stack is empty** - confirming that the function body itself is corrupted

## Root Cause Confirmed

The error occurs in `Cons.cs:45`:
```csharp
public object? Head
{
    get
    {
        if (IsEmpty) throw new InvalidOperationException("The empty list does not have a head.");
        return _head;
    }
}
```

This happens when:
1. `JsFunction` is created with a function body (Cons)
2. The function body is stored in `_body` field
3. When the function is invoked, `Evaluator.EvaluateBlock(_body, ...)` is called
4. The evaluator tries to access `_body.Head`
5. But `_body` is an empty Cons, so the exception is thrown

## Next Investigation Steps

1. **Why is `_body` empty?**
   - Check how functions are parsed and stored
   - Verify that function bodies are correctly captured during parsing
   - Investigate if global scope functions have different parsing behavior

2. **When does the corruption occur?**
   - During parsing?
   - During storage in the global object?
   - During retrieval from global scope?
   - During Symbol.iterator invocation?

3. **Test different function definition styles**
   - Regular function: `function next() {}`
   - Arrow function: `next: () => {}`
   - Method shorthand: `next() {}`
   - See if any style preserves the function body

## Usage in Tests

To capture exceptions in tests:

```csharp
var engine = new JsEngine();

await engine.Run("/* JavaScript code */");

// Read exceptions
var exceptions = new List<ExceptionInfo>();
while (engine.Exceptions().TryRead(out var ex))
{
    exceptions.Add(ex);
    Console.WriteLine($"Exception: {ex.Message}");
    Console.WriteLine($"Context: {ex.Context}");
}
```

## Benefits

1. **Better debugging** - Can see exactly what exceptions occur
2. **Non-intrusive** - Doesn't change exception handling behavior
3. **Test visibility** - Tests can assert on expected exceptions
4. **Call stack tracking** - Captures where in JavaScript the exception occurred
