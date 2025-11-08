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

### 🚧 Not Yet Implemented

- Async/await, Promises
- Destructuring
- Regular expressions
- Standard library (Array methods, Math, Date, JSON, etc.)
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

- **No Standard Library**: Array methods like `map`, `filter`, `reduce` are not available
- **No Async**: Promises and async/await are not supported
- **String Literals**: Only double-quoted strings are supported (no single quotes or template literals)
- **Semicolons**: Statement-ending semicolons are required
- **Number Types**: All numbers are treated as doubles (no BigInt)

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

See [LICENSE](LICENSE) file for details.

## Credits

Developed by Asynkron