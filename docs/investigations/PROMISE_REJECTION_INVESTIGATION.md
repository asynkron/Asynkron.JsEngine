# Promise Rejection Investigation

## Test H Results: CRITICAL DISCOVERY

### What We Found

When testing promise rejection handling, we discovered something unexpected:

```javascript
let globalIterable = {
    [Symbol.iterator]() {
        return {
            next: function() {
                log('next() will be called and should throw');
                // NO return statement - undefined return
            }
        };
    }
};
```

**Result**: The loop ENTERS and runs INFINITELY!

```
LOG: next() will be called and should throw
LOG: In loop (should not reach here)
LOG: next() will be called and should throw
LOG: In loop (should not reach here)
... [infinite loop]
```

### Key Insights

1. **Function body is NOT empty** - the `log()` call executes ✅
2. **next() IS being called** - repeatedly ✅
3. **Loop DOES enter** - "In loop" message appears ✅
4. **But no `done` handling** - loop never terminates ❌

### The Real Issue

The problem is NOT that the function body is empty. The problem is that:

1. When `next()` has no return statement → returns `undefined`
2. `undefined` is not an object with `{value, done}` properties
3. Accessing `undefined.done` returns `undefined` (falsy)
4. Loop thinks iteration should continue
5. Infinite loop!

### Why Previous Tests Showed "Empty List" Error

Looking back at previous tests that used method shorthand:

```javascript
next() {
    return { value: index++, done: false };
}
```

This DID fail with "The empty list does not have a head" error. So there IS a difference between:
- `next: function() { return {...}; }` - works (but infinite if no return)
- `next() { return {...}; }` - fails with empty Cons error

### Test I Results

All function types work fine when called directly:
- Regular function: `function() { ... }` ✅
- Arrow function: `() => { ... }` ✅
- Method shorthand: `method() { ... }` ✅
- Called from async: all work ✅

**So the issue is SPECIFIC to method shorthand in Symbol.iterator context!**

## Promise Rejection Handling

### Expected Behavior

The CPS transformation adds `.catch()` handlers (lines 1808-1834 in CpsTransformer.cs):

```javascript
__iteratorNext(iterator)
    .then(__result => { /* loop logic */ })
    .catch(__error => __reject(__error))  // Should propagate rejection
```

### Actual Behavior in Test H

When `next()` throws an exception:
1. Exception is caught in `__iteratorNext` ✅
2. Converted to rejected promise ✅
3. BUT the `.catch()` handler is NOT being called ❌
4. Loop continues as if nothing happened ❌

This suggests the rejection is NOT propagating through the promise chain.

## Next Investigation Steps

1. **Why does method shorthand fail?**
   - Parser issue with method shorthand in object literals?
   - Method shorthand body not being captured correctly?
   - Specific to computed property names like `[Symbol.iterator]`?

2. **Why don't promise rejections propagate?**
   - Is the `.catch()` handler being attached?
   - Is `__reject` function available in the scope?
   - Does the event loop process rejection handlers?

3. **Create focused test**
   - Compare `next: function()` vs `next()` in Symbol.iterator
   - Add explicit promise rejection test
   - Check if `.catch()` works for manually rejected promises
