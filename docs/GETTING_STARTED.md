# Getting Started with Asynkron.JsEngine

This guide will help you get started with Asynkron.JsEngine quickly.

## Installation

### Using NuGet Package Manager

```bash
dotnet add package Asynkron.JsEngine
```

### Building from Source

```bash
git clone https://github.com/asynkron/Asynkron.JsEngine.git
cd Asynkron.JsEngine
dotnet build
```

## Your First Script

```csharp
using Asynkron.JsEngine;

var engine = new JsEngine();

// Execute simple JavaScript code
var result = engine.Evaluate("2 + 3 * 4;");
Console.WriteLine(result); // Output: 14
```

## Basic Concepts

### 1. Creating an Engine Instance

```csharp
var engine = new JsEngine();
```

The `JsEngine` class is your main entry point. Each instance maintains its own global environment and state.

### 2. Evaluating JavaScript Code

```csharp
// Evaluate returns the result of the last expression
var result = engine.Evaluate(@"
    let x = 10;
    let y = 20;
    x + y;
");
Console.WriteLine(result); // Output: 30
```

### 3. Working with Functions

```csharp
var fibonacci = engine.Evaluate(@"
    function fib(n) {
        if (n <= 1) return n;
        return fib(n - 1) + fib(n - 2);
    }
    fib(10);
");
Console.WriteLine(fibonacci); // Output: 55
```

### 4. Objects and Properties

```csharp
var person = engine.Evaluate(@"
    let person = {
        name: 'Alice',
        age: 30,
        greet() {
            return 'Hello, ' + this.name;
        }
    };
    person.greet();
");
Console.WriteLine(person); // Output: Hello, Alice
```

## Host Interoperability

### Calling C# from JavaScript

You can register C# functions that JavaScript can call:

```csharp
var engine = new JsEngine();

// Simple function
engine.SetGlobalFunction("log", args =>
{
    Console.WriteLine(string.Join(" ", args));
    return null;
});

engine.Evaluate("log('Hello', 'from', 'JavaScript!');");
// Output: Hello from JavaScript!
```

### Functions with `this` Binding

```csharp
engine.SetGlobalFunction("describe", (thisValue, args) =>
{
    if (thisValue is JsObject obj)
    {
        return $"Object with {obj.Properties.Count} properties";
    }
    return "Not an object";
});

var result = engine.Evaluate(@"
    let obj = { a: 1, b: 2, c: 3 };
    describe.call(obj);
");
Console.WriteLine(result); // Output: Object with 3 properties
```

### Setting Global Values

```csharp
engine.SetGlobal("appVersion", "1.0.0");
engine.SetGlobal("isDebug", true);

var result = engine.Evaluate(@"
    'Version: ' + appVersion + ', Debug: ' + isDebug;
");
Console.WriteLine(result); // Output: Version: 1.0.0, Debug: True
```

## Asynchronous Code

For code that uses Promises, `setTimeout`, or `setInterval`, use `Run()` instead of `Evaluate()`:

```csharp
await engine.Run(@"
    setTimeout(function() {
        console.log('Delayed execution!');
    }, 100);
");
```

### Working with Promises

```csharp
await engine.Run(@"
    let promise = Promise.resolve(42);
    promise.then(function(value) {
        console.log('Promise resolved:', value);
    });
");
```

### Async/Await

```csharp
await engine.Run(@"
    async function fetchData() {
        let data = await Promise.resolve('Hello');
        console.log(data);
        return data;
    }
    
    fetchData();
");
```

## Working with Modules

Set up a module loader to enable ES6 imports:

```csharp
var engine = new JsEngine();

engine.SetModuleLoader(modulePath =>
{
    if (modulePath == "utils.js")
    {
        return @"
            export function greet(name) {
                return 'Hello, ' + name;
            }
            
            export const version = '1.0';
        ";
    }
    
    throw new FileNotFoundException($"Module not found: {modulePath}");
});

var result = engine.Evaluate(@"
    import { greet, version } from 'utils.js';
    greet('World') + ' v' + version;
");
Console.WriteLine(result); // Output: Hello, World v1.0
```

## Error Handling

```csharp
try
{
    engine.Evaluate(@"
        throw new Error('Something went wrong!');
    ");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"JavaScript Error: {ex.Message}");
}
```

## Best Practices

### 1. Reuse Engine Instances

Creating an engine instance is relatively expensive. Reuse instances when possible:

```csharp
// Good: Reuse engine for multiple evaluations
var engine = new JsEngine();
engine.Evaluate("let x = 10;");
engine.Evaluate("let y = x + 5;");

// Less efficient: Creating new engine each time
foreach (var script in scripts)
{
    var engine = new JsEngine(); // Avoid this pattern
    engine.Evaluate(script);
}
```

### 2. Semicolons Are Required

Unlike some JavaScript environments, semicolons are required:

```csharp
// Good
engine.Evaluate("let x = 10; let y = 20;");

// Will fail
// engine.Evaluate("let x = 10 let y = 20");
```

### 3. Use Bracket Notation for Reserved Keywords

When accessing properties that are JavaScript reserved words:

```csharp
// Use bracket notation for 'catch', 'finally', etc.
await engine.Run(@"
    promise['catch'](function(error) {
        console.log(error);
    });
");
```

### 4. Handle Type Conversions

The engine uses JavaScript's type system, which may differ from C#:

```csharp
var result = engine.Evaluate("typeof null;");
Console.WriteLine(result); // Output: object (JavaScript quirk)

var equality = engine.Evaluate("null == undefined;");
Console.WriteLine(equality); // Output: True (loose equality)
```

## Next Steps

- Read the **[Complete Feature List](FEATURES.md)** to see all supported JavaScript features
- Explore **[Architecture Overview](ARCHITECTURE.md)** to understand how the engine works
- Check out **[API Reference](API_REFERENCE.md)** for detailed API documentation
- See **[Transformation Pipeline](TRANSFORMATIONS.md)** to understand the compilation process

## Common Examples

### Calculator

```csharp
var engine = new JsEngine();

var result = engine.Evaluate(@"
    function calculate(expression) {
        // Simple calculator
        return eval(expression);
    }
    
    calculate('(10 + 5) * 2');
");
Console.WriteLine(result); // Output: 30
```

### Data Processing

```csharp
var engine = new JsEngine();

var result = engine.Evaluate(@"
    let data = [
        { name: 'Alice', score: 85 },
        { name: 'Bob', score: 92 },
        { name: 'Charlie', score: 78 }
    ];
    
    let average = data
        .map(function(student) { return student.score; })
        .reduce(function(sum, score) { return sum + score; }, 0) / data.length;
    
    average;
");
Console.WriteLine(result); // Output: 85
```

### Configuration Processing

```csharp
var engine = new JsEngine();

engine.SetGlobal("environment", "production");

var config = engine.Evaluate(@"
    let config = {
        development: {
            apiUrl: 'http://localhost:3000',
            debug: true
        },
        production: {
            apiUrl: 'https://api.example.com',
            debug: false
        }
    };
    
    config[environment].apiUrl;
");
Console.WriteLine(config); // Output: https://api.example.com
```

## Troubleshooting

### Script Doesn't Execute

Make sure semicolons are present:
```javascript
let x = 10; // Correct
let x = 10  // Will fail
```

### Promise Never Resolves

Use `Run()` instead of `Evaluate()` for async code:
```csharp
await engine.Run(script); // Not engine.Evaluate()
```

### Module Not Found

Ensure your module loader is registered before evaluating import statements:
```csharp
engine.SetModuleLoader(path => { /* load module */ });
```

### Type Errors

Remember JavaScript's dynamic typing:
```csharp
// JavaScript: "5" + 5 = "55" (string concatenation)
// JavaScript: "5" - 5 = 0 (numeric subtraction)
```
