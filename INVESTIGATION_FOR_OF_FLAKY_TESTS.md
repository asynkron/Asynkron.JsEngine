# Investigation Summary: Flaky For-Of TypedArray Resizable Buffer Tests

## Problem Statement
Test262 for-of typedarray resizable buffer tests fail non-deterministically when run in parallel:
- `language/statements/for-of/typedarray-backed-by-resizable-buffer.js`
- `language/statements/for-of/typedarray-backed-by-resizable-buffer-grow-before-end.js`
- `language/statements/for-of/typedarray-backed-by-resizable-buffer-grow-mid-iteration.js`
- `language/statements/for-of/typedarray-backed-by-resizable-buffer-shrink-mid-iteration.js`
- `language/statements/for-of/typedarray-backed-by-resizable-buffer-shrink-to-zero-mid-iteration.js`

## Symptoms
- **Failure Rate**: ~8/10 tests fail in parallel, 0/10 fail when run serially
- **Error**: `ReferenceError: arrayValues is not defined` or `ReferenceError: values is not defined`
- **Location**: During for-of loop body evaluation in Test262 harness function `TestIterationAndResize`

## Investigation History

### What Was Ruled Out
- ✅ **NOT a for-of iteration bug** - Logic is correct
- ✅ **NOT Debug vs Release** - Fails in both configurations  
- ✅ **NOT AST caching corruption** - Uses thread-safe Interlocked.CompareExchange
- ✅ **NOT object pooling** - Tested with all pooling disabled, same failures
- ✅ **NOT pool ownership issues** - Added ownership tracking, no violations detected
- ✅ **NOT environment pool reset** - JsEnvironment.Initialize() properly clears state

### Fixes Applied (But Didn't Solve Issue)
1. Added `Reset()` calls in pool `Rent()` methods (commit 2c9ab3da)
2. Fixed `IteratorDriverState.IRentable.OnRent()` to actually reset state (this PR)
3. Removed duplicate/redundant pool reset calls

### Current Hypothesis
The variable lookup failure suggests environment chain corruption occurs when:
1. Multiple parallel tests share the same cached harness AST via `HarnessProgramCache`
2. Tests execute the `TestIterationAndResize` function simultaneously
3. The for-of loop inside this function tries to access a variable from its enclosing scope
4. Somehow the environment chain gets corrupted, causing the lookup to fail

### Technical Details

#### Shared State
- **HarnessProgramCache**: `ConcurrentDictionary<string, ProgramNode>` - caches parsed harness AST
- **ForEachStatement._cachedPlan**: Cached `IteratorDriverPlan` on AST node (thread-safe creation)
- All parallel tests share the SAME AST nodes for harness code

#### Pool Lifecycle
```
ObjectPool.Rent() → calls IRentable.OnRent() → resets state
    ↓
PoolWrapper.Rent() → returns cleaned object
    ↓
Usage
    ↓  
PoolWrapper.Return() → ObjectPool.Return() → calls IRentable.OnReturn() → resets state
```

All pooled objects (JsEnvironment, IteratorDriverState, ForInDriverState) properly reset on both rent and return.

## Recommended Next Steps

### Short Term: Workaround
Run these tests serially to unblock CI:

**Option A**: Use NUnit configuration
```xml
<RunSettings>
  <NUnit>
    <NumberOfTestWorkers>1</NumberOfTestWorkers>
  </NUnit>
</RunSettings>
```

**Option B**: Exclude from parallel test suite
Add to `Test262Harness.settings.json`:
```json
"ExcludedFiles": [
  "**/language/statements/for-of/typedarray-backed-by-resizable-buffer*.js"
]
```

### Long Term: Deep Investigation

1. **Add Comprehensive Logging**
   - Log environment chain at for-of entry and during variable lookup
   - Log pool rent/return with full state dump
   - Capture thread IDs and test names

2. **Create Minimal Reproduction**
   - Isolate the harness `TestIterationAndResize` function
   - Create standalone test that runs it in parallel
   - Remove Test262 infrastructure to simplify debugging

3. **Investigate Harness Execution**
   - Review `resizableArrayBufferUtils.js` source
   - Check if harness has assumptions about serial execution
   - Consider if harness variables are somehow being shared

4. **Consider Architectural Changes**
   - Disable AST caching for harness files (performance cost)
   - Add synchronization around harness function calls (performance cost)
   - Make environment chain validation more robust

## Files Modified
- `src/Asynkron.JsEngine/Execution/IteratorDriverState.cs`
- `src/Asynkron.JsEngine/Execution/ForInDriverState.cs`

## References
- Original investigation: Issue comments from 2026-01-08 and 2026-01-09
- Pool fix discovery: Commit 2c9ab3da
- This investigation: 2026-01-09
