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
- **Operators**: Arithmetic, logical (`&&`, `||`, `??`), comparison (`===`, `!==`, etc.)
- **Classes**: `class`, `extends`, `super`, `new`
- **Comments**: Single-line `//` comments
- **Type coercion**: Basic truthiness evaluation
- **Arrays**: Array literals, indexing, dynamic length

### 🚧 Not Yet Implemented

- Async/await, Promises
- Destructuring
- Spread/rest operators
- Template literals
- Regular expressions
- Standard library (Array methods, Math, Date, JSON, etc.)
- Getters/setters
- Modules (import/export)
- Ternary operator (`? :`)

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