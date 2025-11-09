# Asynkron.JsEngine

A lightweight JavaScript interpreter written in C# that parses and evaluates JavaScript code using an S-expression intermediate representation.

## Features

Asynkron.JsEngine implements a substantial subset of JavaScript features:

### ✅ Implemented Features

- **Variables**: `let`, `var`, `const` declarations
- **Functions**: Function declarations, function expressions, arrow functions, closures, nested functions
- **Objects**: Object literals, property access (dot & bracket notation), method calls
  - **Object shorthand**: Property shorthand (`{ x, y }`), method shorthand (`{ method() {} }`)
  - **Computed property names**: Dynamic keys (`{ [expr]: value }`)
- **this binding**: Proper context handling in methods
- **Prototypes**: `__proto__` chain for property lookups
- **Control flow**: `if`/`else`, `for`, `while`, `do-while`, `switch`/`case`, `for...in`, `for...of`
- **Error handling**: `try`/`catch`/`finally`, `throw`
- **Operators**: 
  - Arithmetic: `+`, `-`, `*`, `/`, `%`
  - Logical: `&&`, `||`, `??`
  - Comparison: `===`, `!==`, `==`, `!=`, `>`, `<`, `>=`, `<=`
  - Bitwise: `&`, `|`, `^`, `~`, `<<`, `>>`, `>>>`
  - Increment/Decrement: `++`, `--` (both prefix and postfix)
  - Compound assignment: `+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=`, `>>>=`
  - Ternary: `? :`
  - Optional chaining: `?.`
  - Special: `typeof`
- **Classes**: `class`, `extends`, `super`, `new`
- **Comments**: Single-line (`//`) and multi-line (`/* */`) comments
- **Strings**: Both double-quoted (`"..."`) and single-quoted (`'...'`) string literals
- **Type coercion**: Comprehensive type coercion including:
  - Truthiness evaluation (falsy values: false, 0, "", null, undefined, NaN)
  - ToString conversions (arrays join with comma, objects to "[object Object]")
  - ToNumber conversions (empty/whitespace strings to 0, arrays, objects)
  - Loose equality (==) with proper type coercion
  - Null/undefined coercion (null to 0, undefined to NaN in arithmetic)
- **Arrays**: Array literals, indexing, dynamic length
- **Template literals**: Backtick strings with `${}` expression interpolation
- **Getters/setters**: `get`/`set` property accessors in objects and classes
- **Spread/rest operators**: Rest parameters in functions (`...args`), spread in arrays (`[...arr]`), spread in calls (`fn(...args)`)
- **Destructuring**: Array and object destructuring in variable declarations, assignments, and function parameters
- **Timers**: `setTimeout`, `setInterval`, `clearTimeout`, `clearInterval` for scheduling asynchronous work
- **Promises**: Promise constructor, `then`, `catch`, `finally` methods, and static methods (`Promise.resolve`, `Promise.reject`, `Promise.all`, `Promise.race`)
- **Async/await**: Full async function support with `async`/`await` syntax, including error handling
- **Generators**: Generator functions (`function*`, `yield`) with iterator protocol support
- **Event Queue**: Asynchronous task scheduling and event loop integration
- **Regular expressions**: RegExp constructor with `test()`, `exec()` methods, regex literals (`/pattern/flags`), and regex support in string methods (match, search, replace)
- **Modules**: ES6 module system with `import`/`export` syntax, including:
  - Named imports and exports: `import { x, y } from './module.js'`, `export { x, y }`
  - Default imports and exports: `import x from './module.js'`, `export default x`
  - Namespace imports: `import * as name from './module.js'`
  - Export declarations: `export const x = 1`, `export function foo() {}`
  - Re-exports: `export { x } from './other.js'`
  - Side-effect imports: `import './module.js'`
  - Module caching (modules are loaded once and cached)
- **JavaScript oddities**: `typeof null === "object"`, `null == undefined`, proper undefined handling
- **Standard library**: 
  - Math object with constants (PI, E, etc.) and methods (sqrt, pow, sin, cos, floor, ceil, round, etc.)
  - Array methods (map, filter, reduce, forEach, find, findIndex, some, every, join, includes, indexOf, slice, push, pop, shift, unshift, splice, concat, reverse, sort)
  - String methods (charAt, charCodeAt, indexOf, lastIndexOf, substring, slice, toLowerCase, toUpperCase, trim, trimStart, trimEnd, split, replace, startsWith, endsWith, includes, repeat, padStart, padEnd, match, search)
  - Date object with constructor and instance methods (getTime, getFullYear, getMonth, getDate, getDay, getHours, getMinutes, getSeconds, getMilliseconds, toISOString)
  - Date static methods (now, parse)
  - JSON object with parse and stringify methods
  - RegExp constructor with flags (g, i, m) and methods (test, exec)

### 🚧 Not Yet Implemented

See [docs/MISSING_FEATURES.md](docs/MISSING_FEATURES.md) for a comprehensive list of JavaScript features not yet implemented.

## Architecture

The engine works in three phases:

1. **Lexing**: JavaScript source code is tokenized into a stream of tokens
2. **Parsing**: Tokens are parsed into an S-expression tree representation
3. **Evaluation**: The S-expression tree is evaluated using an environment-based interpreter

The S-expression intermediate representation makes the engine similar to Lisp interpreters, with features like:
- Interned symbols for efficient comparison
- Lexical scoping with closures
- First-class functions
- Expression-oriented evaluation

## Getting Started

### Installation

```bash
dotnet add package Asynkron.JsEngine
```

Or build from source:

```bash
git clone https://github.com/asynkron/Asynkron.JsEngine.git
cd Asynkron.JsEngine
dotnet build
```

### Basic Usage

```csharp
using Asynkron.JsEngine;

var engine = new JsEngine();

// Execute JavaScript code
var result = engine.Evaluate("2 + 3 * 4;");
Console.WriteLine(result); // Output: 14
```

### Working with Functions

```csharp
var engine = new JsEngine();

var result = engine.Evaluate(@"
    function fibonacci(n) {
        if (n <= 1) return n;
        return fibonacci(n - 1) + fibonacci(n - 2);
    }
    fibonacci(10);
");

Console.WriteLine(result); // Output: 55
```

### Objects and Methods

```csharp
var engine = new JsEngine();

var result = engine.Evaluate(@"
    let person = {
        name: ""Alice"",
        age: 30,
        greet: function() {
            return ""Hello, "" + this.name;
        }
    };
    person.greet();
");

Console.WriteLine(result); // Output: Hello, Alice
```

### Host Function Interoperability

You can register C# functions that can be called from JavaScript:

```csharp
var engine = new JsEngine();

// Register a host function
engine.SetGlobalFunction("print", args =>
{
    Console.WriteLine(string.Join(" ", args));
    return null;
});

// Call it from JavaScript
engine.Evaluate("print(\"Hello from JavaScript!\", 42);");
```

With `this` binding:

```csharp
engine.SetGlobalFunction("describe", (thisValue, args) =>
{
    if (thisValue is JsObject obj)
    {
        return $"Object with {obj.Properties.Count} properties";
    }
    return "Not an object";
});

engine.Evaluate(@"
    let obj = { a: 1, b: 2 };
    let result = describe.call(obj);
");
```

### Closures

```csharp
var engine = new JsEngine();

var result = engine.Evaluate(@"
    function makeCounter() {
        let count = 0;
        return function() {
            count = count + 1;
            return count;
        };
    }
    
    let counter = makeCounter();
    let a = counter(); // 1
    let b = counter(); // 2
    let c = counter(); // 3
    a + b + c;
");

Console.WriteLine(result); // Output: 6
```

### Template Literals

```csharp
var engine = new JsEngine();

// String interpolation with expressions
var result = engine.Evaluate(@"
    let name = ""Alice"";
    let age = 30;
    let message = `Hello, my name is ${name} and I am ${age} years old.`;
    message;
");
Console.WriteLine(result); // Output: Hello, my name is Alice and I am 30 years old.

// Template literals with complex expressions
var calc = engine.Evaluate(@"
    let a = 10;
    let b = 20;
    `The sum of ${a} and ${b} is ${a + b}`;
");
Console.WriteLine(calc); // Output: The sum of 10 and 20 is 30
```

### Getters and Setters

```csharp
var engine = new JsEngine();

// Getters and setters in object literals
var tempResult = engine.Evaluate(@"
    let thermometer = {
        _celsius: 0,
        get celsius() { return this._celsius; },
        set celsius(c) { this._celsius = c; },
        get fahrenheit() { return this._celsius * 9 / 5 + 32; }
    };
    thermometer.celsius = 100;
    thermometer.fahrenheit;
");
Console.WriteLine(tempResult); // Output: 212

// Getters and setters in classes
var classResult = engine.Evaluate(@"
    class Rectangle {
        constructor(width, height) {
            this.width = width;
            this.height = height;
        }
        get area() {
            return this.width * this.height;
        }
        set area(value) {
            this.width = value / this.height;
        }
    }
    let rect = new Rectangle(5, 10);
    rect.area;
");
Console.WriteLine(classResult); // Output: 50
```

### Spread and Rest Operators

```csharp
var engine = new JsEngine();

// Rest parameters in functions
var restResult = engine.Evaluate(@"
    function sum(first, ...rest) {
        let total = first;
        let i = 0;
        while (i < rest.length) {
            total = total + rest[i];
            i = i + 1;
        }
        return total;
    }
    sum(1, 2, 3, 4, 5);
");
Console.WriteLine(restResult); // Output: 15

// Spread in array literals
var spreadArrayResult = engine.Evaluate(@"
    let arr1 = [1, 2, 3];
    let arr2 = [4, 5, 6];
    let combined = [0, ...arr1, ...arr2, 7];
    combined[3];
");
Console.WriteLine(spreadArrayResult); // Output: 3

// Spread in function calls
var spreadCallResult = engine.Evaluate(@"
    function add(a, b, c) {
        return a + b + c;
    }
    let numbers = [10, 20, 30];
    add(...numbers);
");
Console.WriteLine(spreadCallResult); // Output: 60
```

### Ternary Operator

```csharp
var engine = new JsEngine();

// Simple ternary
var result = engine.Evaluate(@"
    let age = 20;
    let status = age >= 18 ? ""adult"" : ""minor"";
    status;
");
Console.WriteLine(result); // Output: adult

// Nested ternary for grading
var grade = engine.Evaluate(@"
    let score = 85;
    let grade = score >= 90 ? ""A"" : score >= 80 ? ""B"" : score >= 70 ? ""C"" : ""D"";
    grade;
");
Console.WriteLine(grade); // Output: B
```

### Math Object

```csharp
var engine = new JsEngine();

// Mathematical constants
var pi = engine.Evaluate("Math.PI;");
Console.WriteLine(pi); // Output: 3.141592653589793

// Basic math operations
var sqrt = engine.Evaluate("Math.sqrt(16);");
Console.WriteLine(sqrt); // Output: 4

var power = engine.Evaluate("Math.pow(2, 10);");
Console.WriteLine(power); // Output: 1024

// Rounding
var floor = engine.Evaluate("Math.floor(4.7);");
Console.WriteLine(floor); // Output: 4

var ceil = engine.Evaluate("Math.ceil(4.3);");
Console.WriteLine(ceil); // Output: 5

var round = engine.Evaluate("Math.round(4.5);");
Console.WriteLine(round); // Output: 5

// Trigonometry
var sine = engine.Evaluate("Math.sin(Math.PI / 2);");
Console.WriteLine(sine); // Output: 1

// Complex calculations
var hypotenuse = engine.Evaluate(@"
    let a = 3;
    let b = 4;
    Math.sqrt(Math.pow(a, 2) + Math.pow(b, 2));
");
Console.WriteLine(hypotenuse); // Output: 5
```

### Array Methods

```csharp
var engine = new JsEngine();

// map - transform each element
var doubled = engine.Evaluate(@"
    let numbers = [1, 2, 3, 4];
    let doubled = numbers.map(function(x) { return x * 2; });
    doubled[0] + doubled[1] + doubled[2] + doubled[3];
");
Console.WriteLine(doubled); // Output: 20

// filter - select elements that match a condition
var filtered = engine.Evaluate(@"
    let numbers = [1, 2, 3, 4, 5, 6];
    let greaterThanThree = numbers.filter(function(x) { return x > 3; });
    greaterThanThree[""length""];
");
Console.WriteLine(filtered); // Output: 3

// reduce - accumulate values
var sum = engine.Evaluate(@"
    let numbers = [1, 2, 3, 4, 5];
    numbers.reduce(function(acc, x) { return acc + x; }, 0);
");
Console.WriteLine(sum); // Output: 15

// forEach - iterate over elements
engine.Evaluate(@"
    let numbers = [1, 2, 3];
    let sum = 0;
    numbers.forEach(function(x) { sum = sum + x; });
");

// find - get first matching element
var found = engine.Evaluate(@"
    let numbers = [1, 2, 3, 4, 5];
    numbers.find(function(x) { return x > 3; });
");
Console.WriteLine(found); // Output: 4

// some - check if any element matches
var hasLarge = engine.Evaluate(@"
    let numbers = [1, 3, 5, 6];
    numbers.some(function(x) { return x > 5; });
");
Console.WriteLine(hasLarge); // Output: True

// every - check if all elements match
var allPositive = engine.Evaluate(@"
    let numbers = [1, 2, 3, 4];
    numbers.every(function(x) { return x > 0; });
");
Console.WriteLine(allPositive); // Output: True

// join - concatenate elements into string
var joined = engine.Evaluate(@"
    let items = [""a"", ""b"", ""c""];
    items.join(""-"");
");
Console.WriteLine(joined); // Output: a-b-c

// Method chaining
var chained = engine.Evaluate(@"
    let numbers = [1, 2, 3, 4, 5, 6];
    numbers
        .filter(function(x) { return x > 3; })
        .map(function(x) { return x * 2; })
        .reduce(function(acc, x) { return acc + x; }, 0);
");
Console.WriteLine(chained); // Output: 30
Console.WriteLine(hypotenuse); // Output: 5
```

### Date Object

```csharp
var engine = new JsEngine();

// Current time
var now = engine.Evaluate("Date.now();");
Console.WriteLine(now); // Output: milliseconds since epoch

// Create a specific date
var birthday = engine.Evaluate(@"
    let d = new Date(2024, 0, 15);  // January 15, 2024 (months are 0-indexed)
    d.getFullYear();
");
Console.WriteLine(birthday); // Output: 2024

// Get date components
var dateInfo = engine.Evaluate(@"
    let d = new Date(2024, 5, 15, 14, 30, 45);
    let info = {
        year: d.getFullYear(),
        month: d.getMonth(),      // 0-indexed
        date: d.getDate(),
        hours: d.getHours(),
        minutes: d.getMinutes()
    };
    info.year;
");
Console.WriteLine(dateInfo); // Output: 2024

// ISO string format
var isoString = engine.Evaluate(@"
    let d = new Date(2024, 0, 1);
    d.toISOString();
");
Console.WriteLine(isoString); // Output: 2024-01-01T00:00:00.000Z

// Parse date string
var parsed = engine.Evaluate(@"
    Date.parse(""2024-06-15"");
");
Console.WriteLine(parsed); // Output: milliseconds since epoch
```

### JSON Object

```csharp
var engine = new JsEngine();

// Parse JSON string to object
var parsed = engine.Evaluate(@"
    let jsonStr = `{""name"":""Alice"",""age"":30,""city"":""NYC""}`;
    let person = JSON.parse(jsonStr);
    person.name;
");
Console.WriteLine(parsed); // Output: Alice

// Parse JSON array
var arrayParsed = engine.Evaluate(@"
    let jsonStr = `[1,2,3,4,5]`;
    let numbers = JSON.parse(jsonStr);
    numbers[2];
");
Console.WriteLine(arrayParsed); // Output: 3

// Stringify object to JSON
var stringified = engine.Evaluate(@"
    let person = { name: ""Bob"", age: 25, active: true };
    JSON.stringify(person);
");
Console.WriteLine(stringified); // Output: {""name"":""Bob"",""age"":25,""active"":true}

// Stringify array to JSON
var arrayStringified = engine.Evaluate(@"
    let numbers = [1, 2, 3, 4, 5];
    JSON.stringify(numbers);
");
Console.WriteLine(arrayStringified); // Output: [1,2,3,4,5]

// Round-trip conversion
var roundTrip = engine.Evaluate(@"
    let original = { x: 10, y: 20 };
    let json = JSON.stringify(original);
    let restored = JSON.parse(json);
    restored.x + restored.y;
");
Console.WriteLine(roundTrip); // Output: 30
```

### Additional Array Methods

```csharp
var engine = new JsEngine();

// pop - remove last element
var popped = engine.Evaluate(@"
    let numbers = [1, 2, 3, 4];
    let last = numbers.pop();
    last;
");
Console.WriteLine(popped); // Output: 4

// shift - remove first element
var shifted = engine.Evaluate(@"
    let numbers = [10, 20, 30];
    let first = numbers.shift();
    first;
");
Console.WriteLine(shifted); // Output: 10

// unshift - add to beginning
var unshifted = engine.Evaluate(@"
    let numbers = [3, 4];
    numbers.unshift(1, 2);
    numbers[0];
");
Console.WriteLine(unshifted); // Output: 1

// splice - remove and insert
var spliced = engine.Evaluate(@"
    let numbers = [1, 2, 3, 4, 5];
    numbers.splice(2, 2, 99, 100);  // Remove 2 elements at index 2, insert 99, 100
    numbers[2];
");
Console.WriteLine(spliced); // Output: 99

// concat - combine arrays
var concatenated = engine.Evaluate(@"
    let arr1 = [1, 2];
    let arr2 = [3, 4];
    let combined = arr1.concat(arr2, [5, 6]);
    combined[4];
");
Console.WriteLine(concatenated); // Output: 5

// reverse - reverse in place
var reversed = engine.Evaluate(@"
    let numbers = [1, 2, 3, 4];
    numbers.reverse();
    numbers[0];
");
Console.WriteLine(reversed); // Output: 4

// sort - sort with compare function
var sorted = engine.Evaluate(@"
    let numbers = [3, 1, 4, 1, 5, 9, 2, 6];
    numbers.sort(function(a, b) { return a - b; });
    numbers[0];
");
Console.WriteLine(sorted); // Output: 1
```

### Timers (setTimeout/setInterval)

```csharp
var engine = new JsEngine();

// setTimeout - execute code after a delay
engine.Evaluate(@"
    setTimeout(function() {
        console.log(""This runs after 1000ms"");
    }, 1000);
");

// setInterval - execute code repeatedly
engine.Evaluate(@"
    let count = 0;
    let intervalId = setInterval(function() {
        count = count + 1;
        console.log(""Tick:"", count);
        if (count >= 5) {
            clearInterval(intervalId);
        }
    }, 100);
");

// clearTimeout - cancel a scheduled timeout
engine.Evaluate(@"
    let timeoutId = setTimeout(function() {
        console.log(""This will never run"");
    }, 5000);
    clearTimeout(timeoutId);
");

// Note: Use engine.Run() instead of Evaluate() to process the event queue
await engine.Run(@"
    setTimeout(function() {
        console.log(""Async execution complete"");
    }, 50);
");
```

### Promises

```csharp
var engine = new JsEngine();

// Creating and resolving a promise
await engine.Run(@"
    let p = new Promise(function(resolve, reject) {
        resolve(""Success!"");
    });
    
    p.then(function(value) {
        console.log(value); // Output: Success!
    });
");

// Promise chaining
await engine.Run(@"
    Promise.resolve(10)
        .then(function(x) { return x * 2; })
        .then(function(x) { return x + 5; })
        .then(function(x) {
            console.log(x); // Output: 25
        });
");

// Error handling with catch (use bracket notation for reserved keyword)
await engine.Run(@"
    Promise.reject(""Error occurred"")
        [""catch""](function(error) {
            console.log(""Caught:"", error);
        });
");

// Promise.all - wait for multiple promises
await engine.Run(@"
    let p1 = Promise.resolve(1);
    let p2 = Promise.resolve(2);
    let p3 = Promise.resolve(3);
    
    Promise.all([p1, p2, p3]).then(function(values) {
        console.log(""All resolved:"", values[0], values[1], values[2]);
    });
");

// Promise.race - first to settle wins
await engine.Run(@"
    let fast = Promise.resolve(""I'm fast!"");
    let slow = new Promise(function(resolve) {
        setTimeout(function() {
            resolve(""I'm slow..."");
        }, 100);
    });
    
    Promise.race([fast, slow]).then(function(winner) {
        console.log(winner); // Output: I'm fast!
    });
");

// Combining setTimeout and Promises
await engine.Run(@"
    let delayedPromise = new Promise(function(resolve) {
        setTimeout(function() {
            resolve(""Delayed result"");
        }, 100);
    });
    
    delayedPromise.then(function(value) {
        console.log(value); // Output: Delayed result
    });
");
```

### Async/Await

```csharp
var engine = new JsEngine();

// Simple async function
await engine.Run(@"
    async function fetchData() {
        return ""Hello from async"";
    }
    
    fetchData().then(function(result) {
        console.log(result); // Output: Hello from async
    });
");

// Async function with await
await engine.Run(@"
    async function processData() {
        let value1 = await Promise.resolve(10);
        let value2 = await Promise.resolve(20);
        return value1 + value2;
    }
    
    processData().then(function(result) {
        console.log(result); // Output: 30
    });
");

// Async/await with error handling
await engine.Run(@"
    async function riskyOperation() {
        try {
            let result = await Promise.reject(""Something went wrong"");
            return result;
        } catch (error) {
            return ""Caught: "" + error;
        }
    }
    
    riskyOperation().then(function(result) {
        console.log(result); // Output: Caught: Something went wrong
    });
");

// Async with multiple awaits in expressions
await engine.Run(@"
    async function calculate() {
        let sum = (await Promise.resolve(5)) + (await Promise.resolve(10));
        return sum * 2;
    }
    
    calculate().then(function(result) {
        console.log(result); // Output: 30
    });
");
```

### Generators

```csharp
var engine = new JsEngine();

// Simple generator
var result = engine.Evaluate(@"
    function* countUpTo(max) {
        let count = 1;
        while (count <= max) {
            yield count;
            count = count + 1;
        }
    }
    
    let generator = countUpTo(3);
    let first = generator.next().value;   // 1
    let second = generator.next().value;  // 2
    let third = generator.next().value;   // 3
    first + second + third;
");
Console.WriteLine(result); // Output: 6

// Generator with yield expressions
engine.Evaluate(@"
    function* fibonacci() {
        let a = 0;
        let b = 1;
        while (true) {
            yield a;
            let temp = a;
            a = b;
            b = temp + b;
        }
    }
    
    let fib = fibonacci();
    let f1 = fib.next().value;  // 0
    let f2 = fib.next().value;  // 1
    let f3 = fib.next().value;  // 1
    let f4 = fib.next().value;  // 2
    let f5 = fib.next().value;  // 3
");

// Generator iteration
engine.Evaluate(@"
    function* range(start, end) {
        let i = start;
        while (i < end) {
            yield i;
            i = i + 1;
        }
    }
    
    let gen = range(1, 5);
    let sum = 0;
    let result = gen.next();
    while (!result.done) {
        sum = sum + result.value;
        result = gen.next();
    }
");
```

### Modules

ES6 modules with import/export are fully supported. Modules have their own scope and are cached after first load.

```csharp
var engine = new JsEngine();

// Set up a module loader (can load from files, database, network, etc.)
engine.SetModuleLoader(modulePath =>
{
    // For this example, we'll create modules dynamically
    if (modulePath == "math.js")
    {
        return @"
            export function add(a, b) {
                return a + b;
            }
            
            export function subtract(a, b) {
                return a - b;
            }
            
            export const PI = 3.14159;
        ";
    }
    
    if (modulePath == "utils.js")
    {
        return @"
            export default function greet(name) {
                return ""Hello, "" + name + ""!"";
            }
            
            export function uppercase(str) {
                return str.toUpperCase();
            }
        ";
    }
    
    // In a real application, you might load from the file system:
    // return File.ReadAllText(modulePath);
    
    throw new FileNotFoundException($"Module not found: {modulePath}");
});

// Named imports
var result = engine.Evaluate(@"
    import { add, PI } from ""math.js"";
    add(10, 5) + PI;
");
Console.WriteLine(result); // Output: 18.14159

// Default import
engine.Evaluate(@"
    import greet from ""utils.js"";
    greet(""World"");
");

// Mixed imports (default + named)
engine.Evaluate(@"
    import greet, { uppercase } from ""utils.js"";
    uppercase(greet(""alice""));
");

// Namespace import
engine.Evaluate(@"
    import * as math from ""math.js"";
    math.add(5, 3) * math.PI;
");

// Import with aliases
engine.Evaluate(@"
    import { add as sum, subtract as diff } from ""math.js"";
    sum(10, 5) - diff(10, 5);
");
```

You can also use modules to export classes:

```csharp
engine.SetModuleLoader(modulePath =>
{
    if (modulePath == "shapes.js")
    {
        return @"
            export class Rectangle {
                constructor(width, height) {
                    this.width = width;
                    this.height = height;
                }
                
                area() {
                    return this.width * this.height;
                }
            }
            
            export class Circle {
                constructor(radius) {
                    this.radius = radius;
                }
                
                area() {
                    return Math.PI * this.radius * this.radius;
                }
            }
        ";
    }
    throw new FileNotFoundException($"Module not found: {modulePath}");
});

var area = engine.Evaluate(@"
    import { Rectangle, Circle } from ""shapes.js"";
    
    let rect = new Rectangle(5, 10);
    let circle = new Circle(5);
    
    rect.area() + circle.area();
");
Console.WriteLine(area);
```

### String Methods

```csharp
var engine = new JsEngine();

// Character access and search
var result = engine.Evaluate(@"
    let str = ""Hello World"";
    let char = str.charAt(6);        // ""W""
    let code = str.charCodeAt(0);    // 72 (H)
    let index = str.indexOf(""World""); // 6
    char;
");
Console.WriteLine(result); // Output: W

// String manipulation
engine.Evaluate(@"
    let original = ""  JavaScript  "";
    let trimmed = original.trim();           // ""JavaScript""
    let upper = trimmed.toUpperCase();       // ""JAVASCRIPT""
    let lower = upper.toLowerCase();         // ""javascript""
    let substr = lower.substring(0, 4);      // ""java""
");

// Split and join
var words = engine.Evaluate(@"
    let sentence = ""hello,world,test"";
    let parts = sentence.split("","");
    parts[1];
");
Console.WriteLine(words); // Output: world

// String searching and testing
engine.Evaluate(@"
    let email = ""user@example.com"";
    let hasAt = email.includes(""@"");        // true
    let startsWithUser = email.startsWith(""user""); // true
    let endsWithCom = email.endsWith("".com"");     // true
");

// Padding and repeating
var padded = engine.Evaluate(@"
    let num = ""5"";
    num.padStart(3, ""0"");  // ""005""
");
Console.WriteLine(padded); // Output: 005

var repeated = engine.Evaluate(@"
    ""ha"".repeat(3);  // ""hahaha""
");
Console.WriteLine(repeated); // Output: hahaha
```

### Regular Expressions

```csharp
var engine = new JsEngine();

// Basic regex test with constructor
var isValid = engine.Evaluate(@"
    let pattern = new RegExp(""[0-9]+"");
    pattern.test(""abc123"");
");
Console.WriteLine(isValid); // Output: True

// Regex literal syntax (shorter and more idiomatic)
var literalTest = engine.Evaluate(@"
    let pattern = /[0-9]+/;
    pattern.test(""abc123"");
");
Console.WriteLine(literalTest); // Output: True

// Case-insensitive matching with literal
var matches = engine.Evaluate(@"
    let pattern = /HELLO/i;
    pattern.test(""hello world"");
");
Console.WriteLine(matches); // Output: True

// Extracting matches with exec
engine.Evaluate(@"
    let emailPattern = /([a-z]+)@([a-z]+)\.([a-z]+)/i;
    let match = emailPattern.exec(""user@example.com"");
    let username = match[1];   // ""user""
    let domain = match[2];     // ""example""
    let tld = match[3];        // ""com""
");

// Global flag for multiple matches
var allMatches = engine.Evaluate(@"
    let str = ""I have 2 cats and 3 dogs"";
    let matches = str.match(/[0-9]+/g);
    matches.length;
");
Console.WriteLine(allMatches); // Output: 2

// String replace with regex literal
var replaced = engine.Evaluate(@"
    let str = ""hello hello hello"";
    str.replace(/hello/g, ""hi"");
");
Console.WriteLine(replaced); // Output: hi hi hi

// String search with regex
var position = engine.Evaluate(@"
    let str = ""The year is 2024"";
    str.search(/[0-9]+/);
");
Console.WriteLine(position); // Output: 12

// Email validation example with regex literal
var isValidEmail = engine.Evaluate(@"
    function validateEmail(email) {
        return /^[a-z0-9]+@[a-z]+\.[a-z]+$/i.test(email);
    }
    
    let valid = validateEmail(""user@example.com"");   // true
    let invalid = validateEmail(""invalid.email"");    // false
    valid;
");
Console.WriteLine(isValidEmail); // Output: True

// Character classes and escapes
var complexPattern = engine.Evaluate(@"
    let pattern = /\d+\.\d+/;  // Match decimal numbers
    pattern.test(""Price: 19.99"");
");
Console.WriteLine(complexPattern); // Output: True

// Using regex in array methods
var filtered = engine.Evaluate(@"
    let emails = [""user@test.com"", ""invalid"", ""admin@site.org""];
    let valid = emails.filter(function(email) {
        return /@/.test(email);
    });
    valid.length;
");
Console.WriteLine(filtered); // Output: 2
```

### Typeof Operator and Undefined

```csharp
var engine = new JsEngine();

// typeof operator
var typeofNull = engine.Evaluate("typeof null;");
Console.WriteLine(typeofNull); // Output: object (JavaScript oddity!)

var typeofUndefined = engine.Evaluate("typeof undefined;");
Console.WriteLine(typeofUndefined); // Output: undefined

var typeofNumber = engine.Evaluate("typeof 42;");
Console.WriteLine(typeofNumber); // Output: number

// Undefined handling
var isUndefined = engine.Evaluate("let x = undefined; typeof x === \"undefined\";");
Console.WriteLine(isUndefined); // Output: True

// Loose equality oddity: null == undefined
var looseEqual = engine.Evaluate("null == undefined;");
Console.WriteLine(looseEqual); // Output: True

// But strict equality: null !== undefined
var strictEqual = engine.Evaluate("null === undefined;");
Console.WriteLine(strictEqual); // Output: False

// Nullish coalescing with undefined
var coalesce = engine.Evaluate("undefined ?? \"default\";");
Console.WriteLine(coalesce); // Output: default

// Type coercion
var nullArithmetic = engine.Evaluate("null + 5;");
Console.WriteLine(nullArithmetic); // Output: 5 (null coerces to 0)

var undefinedArithmetic = engine.Evaluate("undefined + 5;");
Console.WriteLine(undefinedArithmetic); // Output: NaN (undefined coerces to NaN)
```

### Type Coercion

```csharp
var engine = new JsEngine();

// Array to string conversion
var arrayToString = engine.Evaluate("\"Result: \" + [1, 2, 3];");
Console.WriteLine(arrayToString); // Output: Result: 1,2,3

// Object to string conversion
var objectToString = engine.Evaluate("\"Value: \" + {};");
Console.WriteLine(objectToString); // Output: Value: [object Object]

// Array to number conversion
var emptyArrayToNumber = engine.Evaluate("[] - 0;");
Console.WriteLine(emptyArrayToNumber); // Output: 0

var singleElementArray = engine.Evaluate("[5] - 0;");
Console.WriteLine(singleElementArray); // Output: 5

// Empty string to number
var emptyStringToNumber = engine.Evaluate("\"\" - 0;");
Console.WriteLine(emptyStringToNumber); // Output: 0

// Loose equality with type coercion
var looseEquality1 = engine.Evaluate("0 == \"\";");
Console.WriteLine(looseEquality1); // Output: True

var looseEquality2 = engine.Evaluate("false == \"0\";");
Console.WriteLine(looseEquality2); // Output: True

var looseEquality3 = engine.Evaluate("[5] == 5;");
Console.WriteLine(looseEquality3); // Output: True

// String concatenation with type coercion
var arrayPlusNumber = engine.Evaluate("[1, 2] + 3;");
Console.WriteLine(arrayPlusNumber); // Output: 1,23
```

## Running the Demo

Console application demos are included in the `examples` folder:

### Main Demo
```bash
cd examples/Demo
dotnet run
```

The main demo showcases:
- Basic arithmetic
- Variables and functions
- Closures
- Objects and arrays
- Control flow
- Ternary operator
- Template literals
- Getters/setters
- Spread/rest operators
- Array methods (map, filter, reduce, sort, etc.)
- Math object
- Date object
- JSON parsing and stringification
- Host function interop

### Promise and Timer Demo
```bash
cd examples/PromiseDemo
dotnet run
```

The Promise demo showcases:
- setTimeout and setInterval
- Promise creation and resolution
- Promise chaining
- Error handling with catch
- Promise.all and Promise.race
- Integration of timers with Promises
- Event queue processing
- JSON parsing and stringification
- Host function interop

## Building and Testing

Build the solution:

```bash
dotnet build
```

Run tests:

```bash
cd tests/Asynkron.JsEngine.Tests
dotnet test
```

## API Reference

### JsEngine Class

#### Methods

- `Parse(string source)` - Parses JavaScript source into an S-expression representation
- `Evaluate(string source)` - Parses and evaluates JavaScript source code
- `Evaluate(Cons program)` - Evaluates an S-expression program
- `SetGlobal(string name, object? value)` - Registers a value in the global scope
- `SetGlobalFunction(string name, Func<IReadOnlyList<object?>, object?> handler)` - Registers a host function
- `SetGlobalFunction(string name, Func<object?, IReadOnlyList<object?>, object?> handler)` - Registers a host function with `this` binding

## Limitations

- **Semicolons**: Statement-ending semicolons are required
- **Number Types**: All numbers are treated as doubles (no BigInt support yet)
- **Reserved Keywords as Properties**: When using reserved keywords like `catch` and `finally` as property names, you must use bracket notation (e.g., `promise["catch"](...)` instead of `promise.catch(...)`)
- **Exponentiation**: Use `Math.pow(x, y)` instead of `x ** y` operator
- **Some Standard Library Methods**: Not all ES6+ standard library methods are implemented (see [docs/MISSING_FEATURES.md](docs/MISSING_FEATURES.md) for details)
## Future Roadmap

The engine has achieved remarkable JavaScript compatibility! It now includes:
- ✅ Full ES6 module system (import/export)
- ✅ Async/await and Promises
- ✅ Generators with yield
- ✅ Destructuring (arrays and objects)
- ✅ Spread/rest operators
- ✅ for...of and for...in loops
- ✅ Object property/method shorthand
- ✅ Computed property names
- ✅ Optional chaining (?.)
- ✅ Single-quoted strings
- ✅ Multi-line comments
- ✅ All bitwise operators
- ✅ Increment/decrement operators (++, --)
- ✅ All compound assignment operators
- ✅ Regex literals with full support
- ✅ Template literals
- ✅ Comprehensive type coercion

See [docs/CPS_TRANSFORMATION_PLAN.md](docs/CPS_TRANSFORMATION_PLAN.md) for async/await implementation details and [docs/DESTRUCTURING_IMPLEMENTATION_PLAN.md](docs/DESTRUCTURING_IMPLEMENTATION_PLAN.md) for destructuring details.

For information about alternative approaches to implementing control flow (return, break, continue), see [docs/CONTROL_FLOW_ALTERNATIVES.md](docs/CONTROL_FLOW_ALTERNATIVES.md).

### Educational Documentation

Learn about alternative evaluation approaches:
- [Bytecode Compilation](docs/BYTECODE_COMPILATION.md) - How to transform the recursive evaluator to use bytecode and a virtual machine
- [Iterative Evaluation](docs/ITERATIVE_EVALUATION.md) - How to transform from recursive to iterative evaluation using explicit stacks

### What's Still Missing?

For a comprehensive list of JavaScript features not yet implemented and their priority, see [docs/MISSING_FEATURES.md](docs/MISSING_FEATURES.md). This document provides:
- Categorized list of missing features with code examples
- Priority rankings (High/Medium/Low)
- Implementation complexity estimates
- Use cases for each feature
- Recommended implementation phases

The most notable remaining features include:
- Exponentiation operator (**)
- Symbol type (for advanced iterators)
- Map and Set collections
- Object static methods (Object.assign, Object.entries, etc.)
- Array static methods (Array.isArray, Array.from, etc.)
- Private class fields
- Proxy and Reflect (advanced metaprogramming)
- BigInt (arbitrary precision integers)
- Typed Arrays (for binary data)

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

See [LICENSE](LICENSE) file for details.

## Credits

Developed by Asynkron