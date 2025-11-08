# Important Missing Features

This document catalogs significant JavaScript features not yet implemented in Asynkron.JsEngine, organized by category and priority. This serves as a roadmap for future development.

## Priority Legend
- 🔴 **High Priority** - Commonly used features that significantly limit practical use
- 🟡 **Medium Priority** - Useful features that enhance capability but have workarounds
- 🟢 **Low Priority** - Nice-to-have features that are less commonly used

---

## 🔴 High Priority Features

### 1. ES6 Modules (import/export)
**Status:** Not Implemented  
**Impact:** Critical for modern JavaScript development

```javascript
// Not yet supported
import { func } from './module.js';
export function myFunc() { }
export default class MyClass { }
```

**Use Cases:**
- Code organization and modularity
- Dependency management
- Third-party library integration
- Standard in modern JavaScript development

**Implementation Complexity:** High
- Requires module resolution system
- Need to handle circular dependencies
- Must support static and dynamic imports
- Needs export binding tracking

---

### 2. Single-Quoted Strings
**Status:** Not Implemented  
**Impact:** High - Many JavaScript codebases use single quotes

```javascript
// Not yet supported
let message = 'Hello World';
let char = 'x';

// Currently must use:
let message = "Hello World";
```

**Use Cases:**
- Code compatibility with existing JavaScript
- Developer preference (many style guides prefer single quotes)
- String literals containing double quotes

**Implementation Complexity:** Low
- Straightforward lexer change
- Already have string parsing logic

---

### 3. Object Methods Shorthand
**Status:** Not Implemented  
**Impact:** Medium-High - Common in modern JavaScript

```javascript
// Not yet supported
let obj = {
    name: "Alice",
    greet() {
        return "Hello " + this.name;
    }
};

// Currently must use:
let obj = {
    name: "Alice",
    greet: function() {
        return "Hello " + this.name;
    }
};
```

**Use Cases:**
- Cleaner object literal syntax
- Modern JavaScript coding style
- Framework/library compatibility

**Implementation Complexity:** Medium
- Parser change to recognize method shorthand
- Similar to existing function parsing

---

### 4. Object Property Shorthand
**Status:** Not Implemented  
**Impact:** Medium-High - Very common in modern JavaScript

```javascript
// Not yet supported
let name = "Alice";
let age = 30;
let person = { name, age };

// Currently must use:
let person = { name: name, age: age };
```

**Use Cases:**
- Cleaner syntax for object construction
- Reduces duplication
- Standard in modern frameworks (React, Vue, etc.)

**Implementation Complexity:** Low
- Parser change to handle implicit key-value pairs

---

### 5. Computed Property Names
**Status:** Not Implemented  
**Impact:** Medium-High - Important for dynamic object construction

```javascript
// Not yet supported
let propName = "dynamicKey";
let obj = {
    [propName]: "value",
    ["computed" + "Key"]: 123
};

// Currently must use:
let obj = {};
obj[propName] = "value";
obj["computed" + "Key"] = 123;
```

**Use Cases:**
- Dynamic property names
- Metaprogramming
- Framework-driven development

**Implementation Complexity:** Medium
- Parser change to handle bracket syntax in object literals
- Expression evaluation at object construction time

---

## 🟡 Medium Priority Features

### 6. for...of Loop
**Status:** Not Implemented  
**Impact:** Medium - Convenient for array iteration

```javascript
// Not yet supported
let numbers = [1, 2, 3, 4, 5];
for (let num of numbers) {
    console.log(num);
}

// Currently must use:
let numbers = [1, 2, 3, 4, 5];
for (let i = 0; i < numbers.length; i++) {
    console.log(numbers[i]);
}
// or
numbers.forEach(function(num) {
    console.log(num);
});
```

**Use Cases:**
- Cleaner array iteration
- Works with any iterable (arrays, strings, etc.)
- More readable than traditional for loops

**Implementation Complexity:** Medium
- Requires iterator protocol implementation
- Need Symbol.iterator support

---

### 7. for...in Loop
**Status:** Not Implemented  
**Impact:** Medium - Useful for object property iteration

```javascript
// Not yet supported
let person = { name: "Alice", age: 30, city: "NYC" };
for (let key in person) {
    console.log(key + ": " + person[key]);
}

// Currently must use:
let person = { name: "Alice", age: 30, city: "NYC" };
let keys = Object.keys(person);
for (let i = 0; i < keys.length; i++) {
    let key = keys[i];
    console.log(key + ": " + person[key]);
}
```

**Use Cases:**
- Object property enumeration
- Object inspection and debugging
- Working with dynamic objects

**Implementation Complexity:** Low-Medium
- Need to expose object property enumeration
- Parser change for for...in syntax

---

### 8. Symbol Type
**Status:** Not Implemented  
**Impact:** Medium - Required for proper iterator protocol

```javascript
// Not yet supported
let sym = Symbol("description");
let obj = {
    [Symbol.iterator]: function*() {
        yield 1;
        yield 2;
        yield 3;
    }
};
```

**Use Cases:**
- Unique property keys
- Iterator protocol (Symbol.iterator)
- Well-known symbols (Symbol.toStringTag, etc.)
- Avoiding property name collisions

**Implementation Complexity:** Medium-High
- New primitive type
- Global symbol registry
- Well-known symbols implementation

---

### 9. Object Static Methods
**Status:** Partially Implemented  
**Impact:** Medium - Common utility functions

**Missing Methods:**
```javascript
// Not yet supported
Object.assign(target, ...sources)      // Copy properties
Object.entries(obj)                     // [[key, value], ...]
Object.values(obj)                      // [value1, value2, ...]
Object.fromEntries(entries)             // Reverse of entries
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

**Implementation Complexity:** Medium
- Each method needs separate implementation
- Some require property descriptor support

---

### 10. Array Static Methods
**Status:** Not Implemented  
**Impact:** Medium - Useful utilities

```javascript
// Not yet supported
Array.isArray(value)           // Check if value is array
Array.from(arrayLike)          // Convert array-like to array
Array.of(element0, element1)   // Create array from arguments
```

**Use Cases:**
- Type checking
- Array creation and conversion
- Working with array-like objects

**Implementation Complexity:** Low-Medium
- Straightforward implementations
- Array.from requires iterable support

---

### 11. Additional Array Methods
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

### 12. Additional String Methods
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

### 13. Multi-Line Comments
**Status:** Not Implemented  
**Impact:** Medium - Convenient for documentation

```javascript
// Not yet supported
/*
 * Multi-line comment
 * spanning multiple lines
 */
let x = 5;

/**
 * JSDoc-style comment
 * @param {number} x
 * @returns {number}
 */
function double(x) { return x * 2; }
```

**Use Cases:**
- Block comments
- JSDoc documentation
- Temporarily commenting out code blocks

**Implementation Complexity:** Low
- Lexer change to skip /* ... */

---

### 14. Exponentiation Operator (**)
**Status:** Not Implemented  
**Impact:** Low-Medium - Convenience feature

```javascript
// Not yet supported
let result = 2 ** 10;  // 1024

// Currently must use:
let result = Math.pow(2, 10);
```

**Use Cases:**
- Cleaner exponentiation syntax
- Mathematical calculations

**Implementation Complexity:** Low
- Lexer and parser change
- Reuse existing Math.pow logic

---

### 15. Bitwise Operators
**Status:** Not Implemented  
**Impact:** Medium - Important for low-level operations

```javascript
// Not yet supported
let a = 5 & 3;      // AND
let b = 5 | 3;      // OR
let c = 5 ^ 3;      // XOR
let d = ~5;         // NOT
let e = 5 << 2;     // Left shift
let f = 5 >> 2;     // Right shift
let g = 5 >>> 2;    // Unsigned right shift
```

**Use Cases:**
- Bit manipulation
- Performance optimizations
- Flags and permissions
- Binary protocols

**Implementation Complexity:** Medium
- Need to handle integer conversion
- Multiple new operators

---

### 16. Increment/Decrement Operators
**Status:** Not Implemented  
**Impact:** Medium - Very common in loops

```javascript
// Not yet supported
let i = 0;
i++;        // Post-increment
++i;        // Pre-increment
i--;        // Post-decrement
--i;        // Pre-decrement

// Currently must use:
let i = 0;
i = i + 1;
```

**Use Cases:**
- Loop counters
- Common programming pattern
- Code readability

**Implementation Complexity:** Medium
- Lexer for ++ and --
- Parser for prefix/postfix distinction
- Proper lvalue handling

---

### 17. Compound Assignment Operators
**Status:** Not Implemented  
**Impact:** Medium - Convenient and common

```javascript
// Not yet supported
x += 5;    // x = x + 5
x -= 5;    // x = x - 5
x *= 5;    // x = x * 5
x /= 5;    // x = x / 5
x %= 5;    // x = x % 5
x **= 2;   // x = x ** 2
x &= 5;    // x = x & 5
x |= 5;    // x = x | 5
x ^= 5;    // x = x ^ 5
x <<= 2;   // x = x << 2
x >>= 2;   // x = x >> 2
x >>>= 2;  // x = x >>> 2
```

**Use Cases:**
- Concise assignment
- Common programming idiom
- Reduces duplication

**Implementation Complexity:** Low-Medium
- Parser change to recognize compound operators
- Transform to standard assignment

---

## 🟢 Low Priority Features

### 18. WeakMap and WeakSet
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

### 19. Map and Set
**Status:** Not Implemented  
**Impact:** Low-Medium - Alternative data structures

```javascript
// Not yet supported
let map = new Map();
map.set("key", "value");
map.get("key");

let set = new Set();
set.add(1);
set.has(1);
```

**Use Cases:**
- Key-value storage with any key type
- Unique value collections
- Better performance than objects for frequent additions/deletions

**Implementation Complexity:** Medium
- New collection types
- Iterator support needed

---

### 20. Proxy and Reflect
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

### 21. Typed Arrays
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

### 22. Async Iteration (for await...of)
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

### 23. Optional Chaining (?.)
**Status:** Not Implemented  
**Impact:** Medium - Very convenient for null checking

```javascript
// Not yet supported
let city = person?.address?.city;
let result = obj.method?.();

// Currently must use:
let city = person && person.address && person.address.city;
```

**Use Cases:**
- Safe property access
- Reduces null checking boilerplate
- Cleaner code

**Implementation Complexity:** Medium
- Parser support for ?. operator
- Short-circuit evaluation

---

### 24. Nullish Coalescing Assignment (??=)
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

### 25. Logical Assignment Operators
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

### 26. BigInt
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

### 27. Private Class Fields
**Status:** Not Implemented  
**Impact:** Medium - Important for encapsulation

```javascript
// Not yet supported
class Counter {
    #count = 0;
    
    increment() {
        this.#count++;
    }
    
    get value() {
        return this.#count;
    }
}
```

**Use Cases:**
- True private members
- Encapsulation
- Data hiding

**Implementation Complexity:** Medium-High
- Parser support for # prefix
- Private field storage mechanism
- Access control

---

### 28. Static Class Fields and Methods
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

### 29. Additional Math Methods
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

### 30. Error Types
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

### 31. Number Static Methods
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

### 32. String.raw
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

### 33. Object Rest/Spread
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

### 34. Tagged Template Literals
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

### 35. Modulo Operator (%)
**Status:** Not Implemented  
**Impact:** Medium - Common mathematical operation

```javascript
// Not yet supported
let remainder = 10 % 3;  // 1

// Currently must use custom function
```

**Use Cases:**
- Finding remainders
- Cyclic patterns
- Even/odd checks
- Array index wrapping

**Implementation Complexity:** Low
- Simple operator addition

---

### 36. Conditional (Ternary) Operator Chaining
**Status:** Implemented (already works!)  
**Note:** This is actually already implemented and shown in the README.

---

### 37. Label Statements
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

### 38. with Statement
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

### 39. Strict Mode
**Status:** Not Implemented  
**Impact:** Low-Medium - Better error checking

```javascript
// Not yet supported
"use strict";
```

**Use Cases:**
- Catch common mistakes
- Prevent certain actions
- Disable deprecated features

**Implementation Complexity:** Medium-High
- Many behavior changes required
- Parser and evaluator changes

---

### 40. eval() Function
**Status:** Not Implemented  
**Impact:** Low - Security risk, rarely needed

```javascript
// Not yet supported
eval("let x = 5; x * 2;");
```

**Use Cases:**
- Dynamic code execution
- Usually considered bad practice

**Implementation Complexity:** Low (can reuse Evaluate method)
**Security Note:** Consider security implications

---

## Summary of Recommendations

### Highest Value (Quick Wins)
1. **Single-quoted strings** - Very easy, high compatibility benefit
2. **Object property/method shorthand** - Medium effort, very common in modern JS
3. **Modulo operator (%)** - Very easy, commonly used
4. **Increment/Decrement operators (++, --)** - Medium effort, very common
5. **Compound assignment operators (+=, -=, etc.)** - Easy, common pattern
6. **Multi-line comments (/* */)** - Easy, important for documentation

### Most Important for Compatibility
1. **ES6 Modules (import/export)** - Critical for modern JavaScript
2. **for...of and for...in loops** - Common iteration patterns
3. **Object.assign() and Object.keys()** - Frequently used utilities
4. **Optional chaining (?.)** - Very popular modern feature
5. **Bitwise operators** - Important for certain algorithms

### Foundation for Future Features
1. **Symbol type** - Required for proper iterators
2. **Map and Set** - Standard collections
3. **Private class fields** - Modern class features
4. **Array.isArray() and Array.from()** - Essential utilities

### Consider Carefully
- **BigInt** - Complex, specialized use case
- **Proxy/Reflect** - Very complex, niche use case
- **Typed Arrays** - Complex, specialized (binary data)
- **eval()** - Security concerns
- **with statement** - Deprecated, don't implement

---

## Implementation Strategy

### Phase 1: Quick Wins (Low Hanging Fruit)
- Single-quoted strings
- Multi-line comments
- Modulo operator (%)
- Object property shorthand
- Computed property names

### Phase 2: Common Operations
- Increment/Decrement operators
- Compound assignment operators
- Exponentiation operator (**)
- for...in loops
- Bitwise operators

### Phase 3: Modern JavaScript Features
- for...of loops (requires Symbol.iterator)
- Object rest/spread
- Optional chaining
- Object.assign and other Object methods
- Array utility methods

### Phase 4: Advanced Features
- ES6 Modules
- Symbol type
- Map and Set
- Private class fields
- Static class fields

### Phase 5: Specialized Features
- Proxy and Reflect
- BigInt
- Typed Arrays
- Tagged template literals

---

## Notes

This document reflects the state of Asynkron.JsEngine as of the time of writing. The engine already implements an impressive array of features including:
- Comprehensive async/await and Promise support
- Generators with yield
- Destructuring
- Spread/rest operators (arrays and function calls)
- Regular expressions with literals
- Template literals
- Classes with inheritance
- Comprehensive standard library (Array, String, Math, Date, JSON, RegExp)
- Proper type coercion
- Event queue and timers

The missing features listed here represent opportunities for further development to achieve greater ECMAScript compatibility and developer convenience.
