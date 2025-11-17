# API Reference

Complete C# API documentation for Asynkron.JsEngine.

## JsEngine Class

The main entry point for executing JavaScript code.

### Namespace

```csharp
using Asynkron.JsEngine;
```

### Constructor

```csharp
public JsEngine()
```

Creates a new instance of the JavaScript engine with an empty global environment.

**Example:**
```csharp
var engine = new JsEngine();
```

---

## Core Methods

### Evaluate

Parses and evaluates JavaScript code synchronously.

```csharp
public object? Evaluate(string source)
```

**Parameters:**
- `source` - JavaScript source code as a string

**Returns:**
- The result of evaluating the last expression in the source code
- Returns `null` if the last statement doesn't produce a value

**Throws:**
- `InvalidOperationException` - If a JavaScript error occurs during evaluation
- `ParseException` - If the source code has syntax errors

**Example:**
```csharp
var engine = new JsEngine();
var result = engine.Evaluate("2 + 3 * 4;");
Console.WriteLine(result); // Output: 14
```

**Note:** For code using Promises, setTimeout, or setInterval, use `Run()` instead.

---

### Run

Parses and evaluates JavaScript code asynchronously, processing the event queue.

```csharp
public async Task<object?> Run(string source)
```

**Parameters:**
- `source` - JavaScript source code as a string

**Returns:**
- A `Task` that completes when all queued async operations finish
- The task result is the last evaluated value

**Example:**
```csharp
var engine = new JsEngine();

await engine.Run(@"
    setTimeout(function() {
        console.log('Async execution!');
    }, 100);
");
```

**Use When:**
- Working with Promises
- Using setTimeout or setInterval
- Using async/await functions

---

### Parse

Parses JavaScript source code into the typed AST.

```csharp
public ProgramNode Parse(string source)
```

**Parameters:**
- `source` - JavaScript source code as a string

**Returns:**
- A `ProgramNode` representing the parsed syntax tree

**Throws:**
- `ParseException` - If the source code has syntax errors

**Example:**
```csharp
var engine = new JsEngine();
var typed = engine.Parse("let x = 10; x + 5;");
Console.WriteLine(typed);
// Output: ProgramNode { ... }
```

---

### ParseWithTransformationSteps

Parses JavaScript and returns typed AST snapshots for the original, constant-folded, and CPS-transformed stages.

```csharp
public (ProgramNode original, ProgramNode constantFolded, ProgramNode cpsTransformed) ParseWithTransformationSteps(string source)
```

**Parameters:**
- `source` - JavaScript source code as a string

**Returns:**
- A tuple containing:
  - `original` - The typed AST straight from the parser
  - `constantFolded` - The AST after typed constant folding
  - `cpsTransformed` - The AST after CPS transformation (or the constant-folded tree if no CPS rewrite was required)

**Example:**
```csharp
var engine = new JsEngine();
var (_, _, cpsTransformed) = engine.ParseWithTransformationSteps(@"
    async function test() {
        return await Promise.resolve(42);
    }
");

Console.WriteLine("CPS transformed:");
Console.WriteLine(cpsTransformed);
```

---

## Global Environment Methods

### SetGlobal

Registers a value in the global JavaScript environment.

```csharp
public void SetGlobal(string name, object? value)
```

**Parameters:**
- `name` - The global variable name
- `value` - The value to set (can be null)

**Example:**
```csharp
var engine = new JsEngine();
engine.SetGlobal("appVersion", "1.0.0");
engine.SetGlobal("isDebug", true);
engine.SetGlobal("maxRetries", 3);

var result = engine.Evaluate("appVersion + ' (debug: ' + isDebug + ')';");
Console.WriteLine(result); // Output: 1.0.0 (debug: True)
```

---

### SetGlobalFunction

Registers a C# function callable from JavaScript.

#### Simple Function (No `this` Binding)

```csharp
public void SetGlobalFunction(string name, Func<IReadOnlyList<object?>, object?> handler)
```

**Parameters:**
- `name` - The global function name
- `handler` - A C# function that takes arguments and returns a result

**Example:**
```csharp
var engine = new JsEngine();

engine.SetGlobalFunction("log", args =>
{
    Console.WriteLine(string.Join(" ", args));
    return null;
});

engine.Evaluate("log('Hello', 'from', 'JavaScript');");
// Output: Hello from JavaScript
```

**Example with Return Value:**
```csharp
engine.SetGlobalFunction("add", args =>
{
    if (args.Count < 2) return null;
    double a = Convert.ToDouble(args[0]);
    double b = Convert.ToDouble(args[1]);
    return a + b;
});

var result = engine.Evaluate("add(10, 20);");
Console.WriteLine(result); // Output: 30
```

#### Function with `this` Binding

```csharp
public void SetGlobalFunction(string name, Func<object?, IReadOnlyList<object?>, object?> handler)
```

**Parameters:**
- `name` - The global function name
- `handler` - A C# function that takes `this` value, arguments, and returns a result

**Example:**
```csharp
var engine = new JsEngine();

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

---

## Module System

### SetModuleLoader

Registers a function to load module source code.

```csharp
public void SetModuleLoader(Func<string, string> loader)
```

**Parameters:**
- `loader` - A function that takes a module path and returns the module source code

**Throws:**
- The loader function should throw `FileNotFoundException` if the module is not found

**Example:**
```csharp
var engine = new JsEngine();

engine.SetModuleLoader(modulePath =>
{
    if (modulePath == "math.js")
    {
        return @"
            export function add(a, b) {
                return a + b;
            }
            
            export const PI = 3.14159;
        ";
    }
    
    // For file-based loading:
    // if (File.Exists(modulePath))
    // {
    //     return File.ReadAllText(modulePath);
    // }
    
    throw new FileNotFoundException($"Module not found: {modulePath}");
});

var result = engine.Evaluate(@"
    import { add, PI } from 'math.js';
    add(10, 5) + PI;
");
Console.WriteLine(result); // Output: 18.14159
```

**Module Caching:**
Modules are loaded once and cached. Subsequent imports of the same module use the cached version.

---

## JavaScript Types in C#

### Type Mapping

| JavaScript Type | C# Type |
|----------------|---------|
| `number` | `double` |
| `string` | `string` |
| `boolean` | `bool` |
| `null` | `null` |
| `undefined` | `null` (or special marker) |
| `object` | `JsObject` |
| `array` | `JsObject` (with numeric indices) |
| `function` | `JsFunction` or `HostFunction` |
| `symbol` | `Symbol` |
| `Promise` | `JsPromise` |
| `Map` | `JsMap` |
| `Set` | `JsSet` |

### JsObject

Represents a JavaScript object.

**Properties:**
```csharp
public Dictionary<string, object?> Properties { get; }
```

**Example:**
```csharp
var engine = new JsEngine();
var result = engine.Evaluate("({ a: 1, b: 2, c: 3 });");

if (result is JsObject jsObj)
{
    Console.WriteLine(jsObj.Properties["a"]); // Output: 1
    Console.WriteLine(jsObj.Properties.Count); // Output: 3
    
    foreach (var kvp in jsObj.Properties)
    {
        Console.WriteLine($"{kvp.Key}: {kvp.Value}");
    }
}
```

### JsFunction

Represents a JavaScript function.

**Method:**
```csharp
public object? Call(object? thisValue, params object?[] args)
```

**Example:**
```csharp
var engine = new JsEngine();
var func = engine.Evaluate("function(x) { return x * 2; }");

if (func is JsFunction jsFunc)
{
    var result = jsFunc.Call(null, 21);
    Console.WriteLine(result); // Output: 42
}
```

### JsPromise

Represents a JavaScript Promise.

**Example:**
```csharp
var engine = new JsEngine();
var promise = engine.Evaluate("Promise.resolve(42)");

if (promise is JsPromise jsPromise)
{
    // Promises are handled internally by the event queue
    // Use engine.Run() to process them
}
```

---

## Exception Handling

### Exception Types

**InvalidOperationException**
- Thrown when JavaScript runtime errors occur
- Contains the error message from JavaScript

**ParseException**
- Thrown when source code has syntax errors
- Contains details about the parsing error

**Example:**
```csharp
var engine = new JsEngine();

try
{
    engine.Evaluate("let x = 10; throw new Error('Custom error');");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"JavaScript error: {ex.Message}");
}

try
{
    engine.Evaluate("let x = ;"); // Syntax error
}
catch (ParseException ex)
{
    Console.WriteLine($"Parse error: {ex.Message}");
}
```

---

## Best Practices

### 1. Reuse Engine Instances

```csharp
// Good: Reuse engine
var engine = new JsEngine();
engine.Evaluate("let x = 10;");
engine.Evaluate("let y = x + 5;");

// Less efficient: New engine each time
foreach (var script in scripts)
{
    var engine = new JsEngine(); // Avoid
    engine.Evaluate(script);
}
```

### 2. Handle Async Code Properly

```csharp
// For synchronous code
var result = engine.Evaluate("2 + 2;");

// For async code (Promises, timers)
await engine.Run(@"
    setTimeout(() => console.log('done'), 100);
");
```

### 3. Type Checking Results

```csharp
var result = engine.Evaluate("({ a: 1, b: 2 });");

switch (result)
{
    case double d:
        Console.WriteLine($"Number: {d}");
        break;
    case string s:
        Console.WriteLine($"String: {s}");
        break;
    case bool b:
        Console.WriteLine($"Boolean: {b}");
        break;
    case JsObject obj:
        Console.WriteLine($"Object with {obj.Properties.Count} properties");
        break;
    case null:
        Console.WriteLine("null or undefined");
        break;
}
```

### 4. Error Handling

Always wrap evaluation in try-catch:

```csharp
try
{
    var result = engine.Evaluate(userScript);
    ProcessResult(result);
}
catch (ParseException ex)
{
    Console.WriteLine($"Syntax error: {ex.Message}");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Runtime error: {ex.Message}");
}
```

---

## Complete Example

```csharp
using Asynkron.JsEngine;

class Program
{
    static async Task Main()
    {
        var engine = new JsEngine();
        
        // Set global values
        engine.SetGlobal("appName", "MyApp");
        engine.SetGlobal("version", "1.0");
        
        // Register host function
        engine.SetGlobalFunction("log", args =>
        {
            Console.WriteLine(string.Join(" ", args));
            return null;
        });
        
        // Register module loader
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "utils.js")
            {
                return "export function greet(name) { return 'Hello, ' + name; }";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        // Execute synchronous code
        try
        {
            var result = engine.Evaluate(@"
                import { greet } from 'utils.js';
                
                log(appName, 'v' + version);
                
                let message = greet('World');
                message;
            ");
            
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        
        // Execute asynchronous code
        await engine.Run(@"
            log('Starting async operation...');
            
            await new Promise(resolve => {
                setTimeout(() => {
                    log('Async complete!');
                    resolve();
                }, 100);
            });
            
            log('All done!');
        ");
    }
}
```

---

## See Also

- **[Getting Started](GETTING_STARTED.md)** - Quick start guide
- **[Supported Features](FEATURES.md)** - Complete feature list
- **[Architecture](ARCHITECTURE.md)** - How the engine works
- **[Transformations](TRANSFORMATIONS.md)** - Code transformation pipeline
