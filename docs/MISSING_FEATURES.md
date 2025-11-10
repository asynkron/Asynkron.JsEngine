# Missing Features

This document catalogs significant JavaScript features not yet implemented in Asynkron.JsEngine, organized by category and priority. This serves as a roadmap for future development.

**Last Updated:** November 2025

> **Note:** For features that have been completed, see [COMPLETED_FEATURES.md](./COMPLETED_FEATURES.md)

## Priority Legend
- 🔴 **High Priority** - Commonly used features that significantly limit practical use
- 🟡 **Medium Priority** - Useful features that enhance capability but have workarounds
- 🟢 **Low Priority** - Nice-to-have features that are less commonly used

---

## 🟡 Medium Priority Features

### 1. Additional Array Methods
**Status:** Not Implemented  
**Impact:** Medium - Useful for array manipulation

```javascript
// Not yet supported
arr.flat(depth)                // Flatten nested arrays
arr.flatMap(callback)          // Map then flatten
arr.at(index)                  // Access by index (supports negative)
arr.fill(value, start, end)    // Fill with static value
arr.copyWithin(target, start, end) // Copy part of array
arr.entries()                  // Iterator of [index, value]
arr.keys()                     // Iterator of indices
arr.values()                   // Iterator of values
arr.findLast(callback)         // Find from end
arr.findLastIndex(callback)    // Find index from end
arr.toSorted(compareFn)        // Non-mutating sort
arr.toReversed()               // Non-mutating reverse
arr.toSpliced(...)             // Non-mutating splice
arr.with(index, value)         // Non-mutating element replacement
```

**Use Cases:**
- Advanced array manipulation
- Functional programming patterns
- Non-mutating operations (newer methods)

**Implementation Complexity:** Low-Medium
- Most are straightforward implementations
- Iterator methods require Symbol.iterator support

---

### 2. Additional String Methods
**Status:** Partially Implemented  
**Impact:** Medium - Useful string utilities

**Missing Methods:**
```javascript
// Not yet supported
str.replaceAll(searchValue, replaceValue) // Replace all occurrences
str.matchAll(regexp)                      // All matches with groups
str.localeCompare(str2)                   // Locale-aware comparison
str.normalize(form)                       // Unicode normalization
str.codePointAt(pos)                      // Full Unicode support
str.fromCodePoint(...codePoints)          // Static method
str.at(index)                             // Access by index (negative support)
str.trimLeft() / str.trimRight()          // Alias for trimStart/trimEnd
str.anchor(name)                          // HTML anchor (legacy)
str.link(url)                             // HTML link (legacy)
```

**Use Cases:**
- Internationalization
- Unicode handling
- Advanced string manipulation
- Legacy HTML generation

**Implementation Complexity:** Low-Medium
- Most are straightforward
- Unicode methods more complex

---

### 3. Object Static Methods (Additional)
**Status:** Partially Implemented  
**Impact:** Medium - Common utility functions

**Missing Methods:**
```javascript
// Not yet supported
Object.freeze(obj)                      // Make immutable
Object.seal(obj)                        // Prevent extensions
Object.isFrozen(obj)                    // Check if frozen
Object.isSealed(obj)                    // Check if sealed
Object.getOwnPropertyNames(obj)         // All property names
Object.getOwnPropertyDescriptor(obj, prop) // Property descriptor
Object.defineProperty(obj, prop, descriptor) // Define property
Object.create(proto)                    // Create with prototype
```

**Use Cases:**
- Object manipulation and inspection
- Immutability patterns
- Property descriptor control
- Prototypal inheritance

---

### 4. Static Class Fields
**Status:** Partially Implemented  
**Impact:** Medium - Useful for class-level data

```javascript
// Static methods work, but not static fields
class MyClass {
    static count = 0;  // Not yet supported
    static #privateStatic = "secret";  // Not yet supported
    
    static increment() {  // Already works
        MyClass.count++;
    }
}
```

**Use Cases:**
- Class-level constants
- Shared state
- Factory methods

**Implementation Complexity:** Low-Medium
- Extend class parsing for static fields

---

### 5. Object Rest/Spread
**Status:** Not Implemented  
**Impact:** Medium - Very useful for object manipulation

```javascript
// Not yet supported
let { x, y, ...rest } = obj;
let merged = { ...obj1, ...obj2 };

// Currently have spread for arrays only
```

**Use Cases:**
- Object copying
- Merging objects
- Extracting properties
- Immutable updates

**Implementation Complexity:** Medium
- Extend existing spread/rest implementation
- Object property enumeration

---

### 6. Error Types
**Status:** Partially Implemented  
**Impact:** Medium - Better error handling

```javascript
// Not yet supported
throw new TypeError("Invalid type");
throw new RangeError("Out of range");
throw new ReferenceError("Undefined variable");
throw new SyntaxError("Invalid syntax");

// Currently can only use:
throw "error message";
throw new Error("error message");
```

**Use Cases:**
- Proper error classification
- Better error handling
- Standard JavaScript errors

**Implementation Complexity:** Low-Medium
- Create error type classes
- Update throw/catch to handle types

---

## 🟢 Low Priority Features

### 7. Tagged Template Literals
**Status:** Not Implemented  
**Impact:** Low - Advanced template feature

```javascript
// Not yet supported
function tag(strings, ...values) {
    return strings[0] + values[0] + strings[1];
}

let message = tag`Hello ${name}!`;
```

**Use Cases:**
- Custom string processing
- DSLs (Domain-Specific Languages)
- SQL query builders
- i18n libraries

**Implementation Complexity:** Medium
- Parser support for tag expressions
- Pass arrays to tag functions

---

### 8. Nullish Coalescing Assignment (??=)
**Status:** Not Implemented  
**Impact:** Low - Convenience feature

```javascript
// Not yet supported
x ??= defaultValue;

// Currently must use:
x = x ?? defaultValue;
```

**Use Cases:**
- Default value assignment
- Complements existing ?? operator

**Implementation Complexity:** Low
- Similar to other compound assignments

---

### 9. Logical Assignment Operators
**Status:** Not Implemented  
**Impact:** Low - Convenience features

```javascript
// Not yet supported
x &&= value;  // x = x && value
x ||= value;  // x = x || value
```

**Use Cases:**
- Conditional assignment
- Guard patterns

**Implementation Complexity:** Low
- Similar to compound assignments

---

### 10. Number Static Methods
**Status:** Not Implemented  
**Impact:** Low-Medium - Useful utilities

```javascript
// Not yet supported
Number.isInteger(value)
Number.isFinite(value)
Number.isNaN(value)
Number.isSafeInteger(value)
Number.parseFloat(string)
Number.parseInt(string, radix)
Number.EPSILON
Number.MAX_SAFE_INTEGER
Number.MIN_SAFE_INTEGER
Number.MAX_VALUE
Number.MIN_VALUE
```

**Use Cases:**
- Number validation
- Type checking
- Parsing strings to numbers

**Implementation Complexity:** Low
- Straightforward implementations

---

### 11. Additional Math Methods
**Status:** Partially Implemented  
**Impact:** Low - Specialized mathematical functions

**Missing Methods:**
```javascript
// Not yet supported
Math.cbrt(x)          // Cube root
Math.clz32(x)         // Count leading zeros
Math.imul(x, y)       // 32-bit integer multiplication
Math.fround(x)        // Round to 32-bit float
Math.hypot(...values) // Hypotenuse (√(x²+y²+z²+...))
Math.acosh(x)         // Inverse hyperbolic cosine
Math.asinh(x)         // Inverse hyperbolic sine
Math.atanh(x)         // Inverse hyperbolic tangent
Math.cosh(x)          // Hyperbolic cosine
Math.sinh(x)          // Hyperbolic sine
Math.tanh(x)          // Hyperbolic tangent
Math.expm1(x)         // e^x - 1
Math.log1p(x)         // ln(1 + x)
```

**Use Cases:**
- Scientific computing
- Specialized calculations

**Implementation Complexity:** Low
- Direct mapping to C# Math methods

---

### 12. String.raw
**Status:** Not Implemented  
**Impact:** Low - Template literal utility

```javascript
// Not yet supported
let path = String.raw`C:\Users\name\Desktop`;
```

**Use Cases:**
- File paths
- Regex patterns
- Raw string literals

**Implementation Complexity:** Medium
- Template literal tag function support needed

---

### 13. Label Statements
**Status:** Not Implemented  
**Impact:** Low - Rarely used

```javascript
// Not yet supported
outerLoop: for (let i = 0; i < 3; i++) {
    innerLoop: for (let j = 0; j < 3; j++) {
        if (i === 1 && j === 1) {
            break outerLoop;
        }
    }
}
```

**Use Cases:**
- Breaking out of nested loops
- Rarely used in modern code

**Implementation Complexity:** Medium
- Label tracking
- Break/continue with labels

---

### 14. WeakMap and WeakSet
**Status:** Not Implemented  
**Impact:** Low - Advanced memory management

```javascript
// Not yet supported
let wm = new WeakMap();
let ws = new WeakSet();
```

**Use Cases:**
- Memory-sensitive caching
- Private data storage
- Preventing memory leaks

**Implementation Complexity:** High
- Requires garbage collection awareness
- Weak references in C#

---

### 15. Proxy and Reflect
**Status:** Not Implemented  
**Impact:** Low - Advanced metaprogramming

```javascript
// Not yet supported
let proxy = new Proxy(target, {
    get(target, prop) {
        return target[prop] * 2;
    }
});
```

**Use Cases:**
- Metaprogramming
- Property access interception
- Virtual properties
- Validation

**Implementation Complexity:** Very High
- Deep integration with object system
- Performance implications

---

### 16. Typed Arrays
**Status:** Not Implemented  
**Impact:** Low - Specialized use cases

```javascript
// Not yet supported
let buffer = new ArrayBuffer(16);
let int32View = new Int32Array(buffer);
let float64View = new Float64Array(buffer);
```

**Use Cases:**
- Binary data manipulation
- WebGL, Canvas, WebAssembly
- Performance-critical operations

**Implementation Complexity:** High
- Multiple typed array types
- ArrayBuffer implementation
- Endianness handling

---

### 17. Async Iteration (for await...of)
**Status:** Not Implemented  
**Impact:** Low - Specialized async use case

```javascript
// Not yet supported
async function* asyncGen() {
    yield 1;
    yield 2;
}

for await (let value of asyncGen()) {
    console.log(value);
}
```

**Use Cases:**
- Async data streams
- Async generators
- Stream processing

**Implementation Complexity:** Medium-High
- Requires async iterators
- Already have async/await and generators separately

---

### 18. BigInt
**Status:** Not Implemented  
**Impact:** Low - Specialized use case

```javascript
// Not yet supported
let big = 9007199254740991n;
let alsoHuge = BigInt(9007199254740991);
```

**Use Cases:**
- Arbitrary precision integers
- Cryptography
- Large number calculations

**Implementation Complexity:** High
- New primitive type
- All operations need BigInt variants
- Type coercion rules

---

### 19. with Statement
**Status:** Not Implemented  
**Impact:** Very Low - Deprecated and discouraged

```javascript
// Not yet supported (and probably shouldn't be)
with (obj) {
    // properties accessed without obj prefix
}
```

**Recommendation:** Do not implement - deprecated in strict mode

---

## Summary of Recommendations

### Highest Value Remaining Features
1. **Additional array methods** - flat, flatMap, at, findLast, findLastIndex, etc.
2. **Additional string methods** - replaceAll, at, matchAll
3. **Object rest/spread** - Immutable update patterns
4. **Static class fields** - Class-level data
5. **Additional Object methods** - freeze, seal, create, defineProperty

### Most Important for Modern JavaScript Compatibility
1. **Object rest/spread** - Common destructuring pattern
2. **Additional array methods** - Useful array manipulation
3. **Static class fields** - Modern class features
4. **Tagged template literals** - Advanced string processing

### Consider Carefully
- **BigInt** - Complex, specialized use case
- **Proxy/Reflect** - Very complex, niche use case
- **Typed Arrays** - Complex, specialized (binary data)
- **with statement** - Deprecated, don't implement

---

## Implementation Strategy

### Phase 5: Next Priority
- Object rest/spread
- Additional array instance methods (flat, flatMap, at, findLast, findLastIndex, etc.)
- Static class fields
- Tagged template literals

### Phase 6: Specialized Features
- Proxy and Reflect
- BigInt
- Typed Arrays
- Async iteration (for await...of)
- WeakMap and WeakSet

---

## Notes

This document reflects the features that are still missing in Asynkron.JsEngine as of November 2025. For a complete list of features that have been successfully implemented, see [COMPLETED_FEATURES.md](./COMPLETED_FEATURES.md).

The engine has achieved remarkable JavaScript compatibility and implements an impressive array of features. For a comprehensive overview of the current state, see [FEATURE_STATUS_SUMMARY.md](./FEATURE_STATUS_SUMMARY.md) and [LARGE_FEATURES_NOT_IMPLEMENTED.md](./LARGE_FEATURES_NOT_IMPLEMENTED.md).

The missing features listed in this document represent opportunities for further development to achieve even greater ECMAScript compatibility and cover more specialized use cases.
