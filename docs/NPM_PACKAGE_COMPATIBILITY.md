# NPM Package Compatibility

Asynkron.JsEngine is designed to run pure JavaScript code without dependencies on Node.js or browser APIs. This document describes what types of npm packages work well with the engine and provides examples.

## Supported Package Types

The following types of npm packages are generally compatible with Asynkron.JsEngine:

### ✅ Pure Computation and Algorithm Packages

These packages perform mathematical calculations, implement algorithms, or manipulate data structures using only JavaScript language features:

- **Mathematical libraries**: Packages that perform calculations (e.g., fibonacci, factorial, prime numbers)
- **String utilities**: Case conversion, padding, formatting (e.g., left-pad, camelCase, kebabCase)
- **Array utilities**: Sorting, filtering, grouping, transforming (e.g., array-shuffle, chunk, flatten)
- **Validation algorithms**: Format checkers and validators (e.g., luhn algorithm for credit cards)
- **Data structure utilities**: Deep equality checking, object merging, cloning

### ✅ Language Feature Polyfills

Packages that implement JavaScript language features using other JavaScript features are compatible:

- Array method polyfills (for older JavaScript versions)
- Object utility functions
- String utility functions

## Incompatible Package Types

### ❌ Node.js-Specific Packages

Packages that require Node.js built-in modules will not work:

- File system operations (`fs` module)
- Network operations (`http`, `https`, `net` modules)
- Operating system utilities (`os`, `path` modules)
- Child processes (`child_process` module)
- Streams (`stream` module)

### ❌ Browser-Specific Packages

Packages that depend on browser APIs will not work:

- DOM manipulation (e.g., jQuery, React, Vue)
- Browser APIs (e.g., `fetch`, `XMLHttpRequest`, `localStorage`)
- Browser events (e.g., mouse, keyboard, touch events)

### ❌ Packages with Native Dependencies

Packages that include compiled native code (C/C++ addons) will not work.

## Tested Packages

The following package implementations have been validated with Asynkron.JsEngine and serve as examples:

### String Utilities

#### left-pad
Pads a string to a certain length with a character.

```javascript
function leftPad(str, len, ch) {
    str = str + '';
    let i = -1;
    if (!ch && ch !== 0) ch = ' ';
    len = len - str.length;
    while (++i < len) {
        str = ch + str;
    }
    return str;
}

leftPad('foo', 5, ' ');  // "  foo"
leftPad('42', 5, '0');   // "00042"
```

#### camelCase
Converts strings to camelCase format.

```javascript
function camelCase(str) {
    let result = '';
    let capitalizeNext = false;
    let i = 0;
    
    while (i < str.length) {
        let char = str.charAt(i);
        
        if (char === '-' || char === '_' || char === ' ') {
            capitalizeNext = true;
        } else {
            if (capitalizeNext) {
                result = result + char.toUpperCase();
                capitalizeNext = false;
            } else {
                result = result + char.toLowerCase();
            }
        }
        
        i = i + 1;
    }
    
    return result;
}

camelCase('hello-world-test');  // "helloWorldTest"
camelCase('foo_bar_baz');       // "fooBarBaz"
```

#### kebabCase
Converts strings to kebab-case format.

```javascript
function kebabCase(str) {
    let result = '';
    let i = 0;
    
    while (i < str.length) {
        let char = str.charAt(i);
        let code = str.charCodeAt(i);
        
        if (code >= 65 && code <= 90) {
            if (i > 0) {
                result = result + '-';
            }
            result = result + char.toLowerCase();
        } else if (char === '_' || char === ' ') {
            result = result + '-';
        } else {
            result = result + char;
        }
        
        i = i + 1;
    }
    
    return result;
}

kebabCase('helloWorldTest');  // "hello-world-test"
```

### Number Utilities

#### is-odd / is-even
Check if a number is odd or even using bitwise operations.

```javascript
function isOdd(num) {
    let i = Math.abs(num);
    return !!(i & 1);
}

function isEven(num) {
    let i = Math.abs(num);
    return !(i & 1);
}

isOdd(3);   // true
isEven(4);  // true
```

#### clamp
Constrains a number within a range.

```javascript
function clamp(value, min, max) {
    return Math.min(Math.max(value, min), max);
}

clamp(5, 0, 10);   // 5
clamp(-5, 0, 10);  // 0
clamp(15, 0, 10);  // 10
```

#### in-range
Checks if a number is within a range.

```javascript
function inRange(value, start, end) {
    if (end === undefined) {
        end = start;
        start = 0;
    }
    return value >= start && value < end;
}

inRange(5, 0, 10);  // true
inRange(15, 0, 10); // false
```

### Algorithm Implementations

#### fibonacci
Generates fibonacci sequence numbers.

```javascript
function fibonacci(n) {
    if (n <= 1) return n;
    return fibonacci(n - 1) + fibonacci(n - 2);
}

// Iterative version (more efficient)
function fibonacciIterative(n) {
    if (n <= 1) return n;
    
    let a = 0;
    let b = 1;
    let i = 2;
    
    while (i <= n) {
        let temp = a + b;
        a = b;
        b = temp;
        i = i + 1;
    }
    
    return b;
}

fibonacci(10);           // 55
fibonacciIterative(10);  // 55
```

#### luhn
Implements the Luhn algorithm for credit card validation.

```javascript
function luhn(cardNumber) {
    let str = cardNumber + '';
    let sum = 0;
    let isEven = false;
    let i = str.length - 1;
    
    while (i >= 0) {
        let digit = str.charCodeAt(i) - 48;
        
        if (isEven) {
            digit = digit * 2;
            if (digit > 9) {
                digit = digit - 9;
            }
        }
        
        sum = sum + digit;
        isEven = !isEven;
        i = i - 1;
    }
    
    return sum % 10 === 0;
}

luhn('4532015112830366');  // true (valid)
luhn('1234567812345678');  // false (invalid)
```

### Array Utilities

#### flatten
Flattens nested arrays one level deep.

```javascript
function flatten(arr) {
    let result = [];
    let i = 0;
    
    while (i < arr.length) {
        let item = arr[i];
        if (typeof item === 'object' && item.length !== undefined) {
            let j = 0;
            while (j < item.length) {
                result.push(item[j]);
                j = j + 1;
            }
        } else {
            result.push(item);
        }
        i = i + 1;
    }
    
    return result;
}

let nested = [1, [2, 3], 4, [5, 6]];
flatten(nested);  // [1, 2, 3, 4, 5, 6]
```

#### chunk
Splits an array into chunks of specified size.

```javascript
function chunk(arr, size) {
    let result = [];
    let i = 0;
    
    while (i < arr.length) {
        let chunk = [];
        let j = 0;
        
        while (j < size && i + j < arr.length) {
            chunk.push(arr[i + j]);
            j = j + 1;
        }
        
        result.push(chunk);
        i = i + size;
    }
    
    return result;
}

let arr = [1, 2, 3, 4, 5];
chunk(arr, 2);  // [[1, 2], [3, 4], [5]]
```

#### unique
Removes duplicate values from an array.

```javascript
function unique(arr) {
    let result = [];
    let i = 0;
    
    while (i < arr.length) {
        let found = false;
        let j = 0;
        
        while (j < result.length) {
            if (result[j] === arr[i]) {
                found = true;
            }
            j = j + 1;
        }
        
        if (!found) {
            result.push(arr[i]);
        }
        
        i = i + 1;
    }
    
    return result;
}

let arr = [1, 2, 2, 3, 3, 3, 4, 5, 5];
unique(arr);  // [1, 2, 3, 4, 5]
```

### Object Utilities

#### deepEqual
Performs deep equality comparison of objects.

```javascript
function deepEqual(a, b) {
    if (a === b) return true;
    
    if (a === null || b === null) return false;
    if (a === undefined || b === undefined) return false;
    
    if (typeof a !== typeof b) return false;
    
    if (typeof a !== 'object') return false;
    
    let keysA = Object.keys(a);
    let keysB = Object.keys(b);
    
    if (keysA.length !== keysB.length) return false;
    
    let i = 0;
    while (i < keysA.length) {
        let key = keysA[i];
        if (!deepEqual(a[key], b[key])) return false;
        i = i + 1;
    }
    
    return true;
}

let obj1 = { a: 1, b: 2, c: { d: 3 } };
let obj2 = { a: 1, b: 2, c: { d: 3 } };
deepEqual(obj1, obj2);  // true
```

## Guidelines for Package Selection

When selecting npm packages to use with Asynkron.JsEngine, consider the following:

1. **Check Dependencies**: Look at `package.json` to ensure the package has no dependencies, or only dependencies on other pure JavaScript packages.

2. **Review the Source**: Verify the package doesn't use:
   - `require()` for Node.js modules (e.g., `require('fs')`, `require('http')`)
   - Browser globals (e.g., `window`, `document`, `navigator`)
   - Process-specific APIs (e.g., `process.env`, `process.argv`)

3. **Look for Pure Functions**: Packages that export pure functions (deterministic outputs for given inputs) are ideal.

4. **Test Thoroughly**: Always test the package with the engine before deploying to production.

## Engine Limitations to Consider

When using npm package code with Asynkron.JsEngine, be aware of these limitations:

1. **Semicolons Required**: Statement-ending semicolons are mandatory.

2. **Function Parameters**: Functions require exact parameter counts. Default parameters are not yet supported.

3. **Variable Declarations**: All `let` and `const` declarations must include an initializer:
   ```javascript
   // NOT supported
   let x;
   
   // Supported
   let x = undefined;
   ```

4. **Reserved Keywords as Properties**: Use bracket notation for reserved keywords:
   ```javascript
   // Use bracket notation
   promise["catch"](handler);
   
   // Instead of dot notation
   promise.catch(handler);
   ```

5. **No String Constructor**: Use string concatenation instead:
   ```javascript
   // NOT supported
   String(value);
   
   // Supported
   value + '';
   ```

## Testing npm Packages

See `tests/Asynkron.JsEngine.Tests/NpmPackageTests.cs` for comprehensive test examples demonstrating how to validate npm package implementations with the engine.

## Contributing

If you've successfully used an npm package with Asynkron.JsEngine, consider contributing:

1. Add test cases to `NpmPackageTests.cs`
2. Document the package in this file
3. Submit a pull request

This helps the community understand what works and serves as validation of the engine's compatibility.
