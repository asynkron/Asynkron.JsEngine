# CPS Loop Transformation Debugging Notes

## Summary
The CPS transformation for loops with await is **structurally correct** and generates valid code that works in Node.js. However, the transformed code does not execute properly in the JsEngine evaluator.

## What Works ✅
1. The transformation logic creates the correct S-expression structure
2. The generated code matches the working Node.js pattern exactly
3. The transformation correctly uses `__loopResolve` wrapper to avoid premature promise resolution
4. Simple function calls inside Promise executors work (verified by passing tests)
5. Nested function definitions and calls work (verified by LoopExecutionDebugTests.NestedFunctionDefAndCall_InPromiseExecutor)

## What Doesn't Work ❌
1. The loop check function appears not to be called when inside a CPS-transformed async function
2. Test output shows "before loop" logs but never shows logs from inside the loop body
3. The result variable remains empty, indicating no iterations executed

## Validated Patterns

###Node.js (works):
```javascript
async function test() {
  return new Promise(function(__resolve, __reject) {
    try {
      let __iterator = arr[Symbol.iterator]();
      
      function __loopCheck() {
        let __result = __iterator.next();
        if (__result.done) {
          __resolve();
        } else {
          function __loopResolve() {
            __loopCheck();  // Recursive call
          }
          
          let item = __result.value;
          Promise.resolve(item).then(function(value) {
            result = result + value;
            __loopResolve();  // Calls loop check for next iteration
          });
        }
      }
      
      __loopCheck();  // Initial call - THIS EXECUTES in Node.js
    } catch (__error) {
      __reject(__error);
    }
  });
}
```

### JsEngine Transformed S-Expression:
```
(function test () 
  (block 
    (return 
      (new Promise 
        (lambda null (__resolve __reject) 
          (block 
            (try 
              (block 
                (let result "") 
                (let __iterator...) 
                (function __loopCheck () ...) 
                (expr-stmt (call __loopCheck)))  <-- NOT EXECUTING
              (catch __error ...) 
              null)))))))
```

## Investigation Areas

### Verified NOT the Issue:
- ❌ Transformation structure - confirmed correct by Node.js execution
- ❌ Promise executor invocation - executor IS called (line 1486 in StandardLibrary.cs)
- ❌ Block statement evaluation - EvaluateBlock correctly iterates through statements
- ❌ Function declaration in blocks - function definitions work normally
- ❌ Expression statement evaluation - expr-stmt correctly evaluates and discards result
- ❌ Try block statement execution - try blocks correctly evaluate their body

### Potential Issues to Investigate:
1. **Lambda vs Function**: The Promise executor is a lambda `(lambda null (__resolve __reject) ...)`. Does lambda evaluation differ from function evaluation in a way that affects inner statements?

2. **Async Context**: The difference between passing tests (regular functions) and failing tests (async functions) suggests something specific to the CPS transformation context.

3. **Statement Ordering**: Are all statements in the try block being evaluated in sequence, or does evaluation stop after the function declaration?

4. **Scope/Environment**: Is the function being defined in the correct scope where it can be called?

5. **Timing**: Is there a synchronous vs asynchronous execution issue where the call happens but in the wrong execution context?

## Debugging Steps for Next Session

1. **Add instrumentation to the evaluator**:
   - Add logging in `EvaluateBlock` to see which statements are being evaluated
   - Add logging in `EvaluateFunctionDeclaration` to confirm function is defined
   - Add logging in function call evaluation to see if the call is attempted

2. **Create minimal repro**:
   - Create a test that manually constructs the exact transformed S-expression
   - Evaluate it directly without going through parsing/transformation
   - See if it executes or fails

3. **Compare working vs non-working**:
   - Take the passing `NestedFunctionDefAndCall_InPromiseExecutor` test
   - Transform it to use a lambda instead of function expression for the executor
   - See if that changes the behavior

4. **Check evaluation context**:
   - Verify that `context.ShouldStopEvaluation` is not being set incorrectly
   - Check if any flow control flags are being set that would stop statement evaluation

## Files to Focus On
- `src/Asynkron.JsEngine/Evaluator.cs` - Statement evaluation logic
- `src/Asynkron.JsEngine/CpsTransformer.cs` - Transformation logic (already correct)
- `tests/Asynkron.JsEngine.Tests/CpsTransformDebugTests.cs` - Debug tests
- `tests/Asynkron.JsEngine.Tests/LoopExecutionDebugTests.cs` - Execution tests

## Quick Win Opportunity
The transformation is correct! We just need to figure out why one specific pattern (function definition + call in try block within lambda within Promise constructor within CPS-transformed async function) doesn't execute when simpler versions of the same pattern do work.
