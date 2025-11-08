# Asynkron.JsEngine

A lightweight JavaScript interpreter written in C# that parses and evaluates JavaScript code using an S-expression intermediate representation.

## Features

Asynkron.JsEngine implements a substantial subset of JavaScript features:

### ✅ Implemented Features

- **Variables**: `let`, `var`, `const` declarations
- **Functions**: Function declarations, function expressions, arrow functions, closures, nested functions
- **Objects**: Object literals, property access (dot & bracket notation), method calls
- **this binding**: Proper context handling in methods
- **Prototypes**: `__proto__` chain for property lookups
- **Control flow**: `if`/`else`, `for`, `while`, `do-while`, `switch`/`case`
- **Error handling**: `try`/`catch`/`finally`, `throw`
- **Operators**: Arithmetic, logical (`&&`, `||`, `??`), comparison (`===`, `!==`, etc.), ternary (`? :`)
- **Classes**: `class`, `extends`, `super`, `new`
- **Comments**: Single-line `//` comments
- **Type coercion**: Basic truthiness evaluation
- **Arrays**: Array literals, indexing, dynamic length
- **Template literals**: Backtick strings with `${}` expression interpolation
- **Getters/setters**: `get`/`set` property accessors in objects and classes
- **Spread/rest operators**: Rest parameters in functions (`...args`), spread in arrays (`[...arr]`), spread in calls (`fn(...args)`)
- **Standard library**: 
  - Math object with constants (PI, E, etc.) and methods (sqrt, pow, sin, cos, floor, ceil, round, etc.)
  - Array methods (map, filter, reduce, forEach, find, findIndex, some, every, join, includes, indexOf, slice, push, pop, shift, unshift, splice, concat, reverse, sort)
  - Date object with constructor and instance methods (getTime, getFullYear, getMonth, getDate, getDay, getHours, getMinutes, getSeconds, getMilliseconds, toISOString)
  - Date static methods (now, parse)
  - JSON object with parse and stringify methods

### 🚧 Not Yet Implemented

- Async/await, Promises (see [CPS Transformation Plan](docs/CPS_TRANSFORMATION_PLAN.md) for implementation roadmap)
- Generators (`function*`, `yield`) (see [CPS Transformation Plan](docs/CPS_TRANSFORMATION_PLAN.md) for implementation roadmap)
- Destructuring
- Regular expressions
- Complex type coercion rules (comprehensive toString, toNumber conversions)
- Modules (import/export)

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

## Running the Demo

A console application demo is included in the `examples/Demo` folder:

```bash
cd examples/Demo
dotnet run
```

The demo showcases:
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

- **No Async**: Promises and async/await are not supported (see [CPS Transformation Plan](docs/CPS_TRANSFORMATION_PLAN.md) for roadmap)
- **No Generators**: Generator functions (`function*`, `yield`) are not supported (see [CPS Transformation Plan](docs/CPS_TRANSFORMATION_PLAN.md) for roadmap)
- **No Regex**: Regular expressions are not implemented
- **No Destructuring**: Destructuring assignments are not supported
- **No Modules**: ES6 import/export is not supported
- **String Literals**: Only double-quoted strings and template literals (backticks) are supported (no single quotes)
- **Semicolons**: Statement-ending semicolons are required
- **Number Types**: All numbers are treated as doubles (no BigInt)
- **Type Coercion**: Only basic type coercion is implemented

## Future Roadmap

See [docs/CPS_TRANSFORMATION_PLAN.md](docs/CPS_TRANSFORMATION_PLAN.md) for a detailed plan on implementing:
- Continuation-Passing Style (CPS) transformation
- Generator functions (`function*`, `yield`)
- Async/await and Promises
- Implementation timeline and phases

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

See [LICENSE](LICENSE) file for details.

## Credits

Developed by Asynkron