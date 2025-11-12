# Asynkron.JsEngine

A lightweight JavaScript interpreter written in C# that parses and evaluates JavaScript code using an S-expression intermediate representation.

## 📚 Documentation

This is the main entry point for all documentation. Please refer to the sections below:

### Getting Started
- **[Quick Start Guide](docs/GETTING_STARTED.md)** - Installation, basic usage, and first steps
- **[API Reference](docs/API_REFERENCE.md)** - Complete API documentation

### Feature Documentation
- **[Supported Features](docs/FEATURES.md)** - Comprehensive list of all implemented JavaScript features with examples
- **[NPM Package Compatibility](docs/NPM_PACKAGE_COMPATIBILITY.md)** - Running npm packages with the engine

### Architecture & Design
- **[Architecture Overview](docs/ARCHITECTURE.md)** - System design, components, and design decisions
- **[Transformation Pipeline](docs/TRANSFORMATIONS.md)** - How JavaScript code transforms through the pipeline (JS → S-Expr → CPS)

### Implementation Details
- **[CPS Transformation Plan](docs/CPS_TRANSFORMATION_PLAN.md)** - Async/await implementation strategy
- **[Destructuring Implementation](docs/DESTRUCTURING_IMPLEMENTATION_PLAN.md)** - Destructuring design and implementation
- **[Control Flow Alternatives](docs/CONTROL_FLOW_ALTERNATIVES.md)** - Alternative approaches for control flow
- **[Bytecode Compilation](docs/BYTECODE_COMPILATION.md)** - Educational: Transform to bytecode VM (alternative approach)
- **[Iterative Evaluation](docs/ITERATIVE_EVALUATION.md)** - Educational: Transform from recursive to iterative evaluation

### Status & Planning
- **[Feature Status](docs/FEATURE_STATUS_SUMMARY.md)** - Current implementation status
- **[Remaining Tasks](docs/REMAINING_TASKS.md)** - What's left to implement
- **[Large Features Not Implemented](docs/LARGE_FEATURES_NOT_IMPLEMENTED.md)** - Analysis of specialized features not yet implemented

---

## Quick Overview

Asynkron.JsEngine implements a substantial subset of JavaScript features:

### ✅ Implemented Features (99% Coverage!)

- ✅ Variables, functions, classes, objects, arrays
- ✅ Async/await, Promises, generators
- ✅ ES6 modules (import/export) including dynamic imports
- ✅ Template literals, destructuring, spread/rest
- ✅ All operators and control flow
- ✅ Comprehensive standard library (Math, Date, JSON, RegExp, etc.)
- ✅ Symbol, Map, Set, WeakMap, WeakSet collections
- ✅ BigInt for arbitrary precision integers
- ✅ Typed Arrays and ArrayBuffer for binary data
- ✅ Async iteration (for await...of)

See **[Complete Feature List](docs/FEATURES.md)** for detailed documentation with examples.

### 🚧 Not Implemented (2 Specialized Features)

Only 2 highly specialized features remain: Proxy/Reflect. See **[Large Features Not Implemented](docs/LARGE_FEATURES_NOT_IMPLEMENTED.md)** for analysis.

Note: BigInt, Typed Arrays, WeakMap/WeakSet, async iteration, and dynamic imports are now implemented!

---

## Running the Demo

Console application demos are included in the `examples` folder:

### Main Demo
```bash
cd examples/Demo
dotnet run
```

The main demo showcases basic features including variables, functions, closures, objects, arrays, control flow, operators, and standard library usage.

### Promise and Timer Demo
```bash
cd examples/PromiseDemo
dotnet run
```

Demonstrates setTimeout, setInterval, Promise creation, chaining, error handling, and event queue processing.

### NPM Package Compatibility Demo
```bash
cd examples/NpmPackageDemo
dotnet run
```

Shows that the engine can run pure JavaScript npm packages without Node.js dependencies.

### S-Expression Demo
```bash
cd examples/SExpressionDemo
dotnet run
```

Displays the S-expression representation and CPS transformation of JavaScript code. See **[Transformation Pipeline](docs/TRANSFORMATIONS.md)** for details.

---

## Building and Testing

```bash
# Build the solution
dotnet build

# Run tests
cd tests/Asynkron.JsEngine.Tests
dotnet test
```

---

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

See [LICENSE](LICENSE) file for details.

## Credits

Developed by Asynkron