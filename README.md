<img src="assets/images/logo.png" width="100%" alt="Asynkron.JsEngine Logo">

# Asynkron.JsEngine

A lightweight JavaScript interpreter written in C# that parses and evaluates JavaScript code through typed AST analysis, expression bytecode, and an IR execution plan.

## 📚 Documentation

Current documentation is split between repository docs and the agent/operator playbooks.

### Getting Started
- Build and test commands are in [Building and Testing](#building-and-testing).
- Console examples live under [`examples/`](examples/), including the basic demo, promise demo, npm package demo, and host demos.

### Architecture and Design
- **[Current architecture deep dive](docs/architecture-current-deep-dive-2026-05-13.md)** - Current execution architecture, IR boundaries, and implementation notes.
- **[Architecture snapshot](docs/architecture-current-2026-05-13.html)** - Rendered architecture reference.
- **[Architecture first-code snapshot](docs/architecture-first-code-2025-11-07.html)** - Earlier architecture artifact kept for historical comparison.

### Status and Decisions
- **[Remaining Test262 gaps](docs/remaining-test262-gaps.md)** - Current standards-compliance gap tracking.
- **[Architecture decision records](docs/adrs/)** - Durable implementation decisions and quality-gate notes.
- **[Agent playbooks](agents/)** - Contributor/operator rules for coding standards, build/test commands, profiling, debugging, and workflow.

The older S-expression/CPS-era documents referenced by early README versions are no longer present in the repository. Use the current architecture docs and ADRs as the source of truth.

---

## Quick Overview

Asynkron.JsEngine targets ECMAScript 262 with full language coverage and a steadily growing built-in library.

### Current Status
- ECMAScript 262 language: 100% compliant; all language-focused Test262 cases pass.
- Built-ins / standard library: ~50% compliant; about half of the ES built-ins are implemented on the attribute-driven generator model and the rest are being migrated. See `docs/remaining-test262-gaps.md` and the internal test suites for current evidence.

### Capabilities
- Variables, functions, classes, objects, arrays
- Async/await, Promises, generators
- ES modules (import/export) including dynamic imports
- Template literals, destructuring, spread/rest, operators, and control flow
- Implemented built-ins include Object, Array/TypedArray/ArrayBuffer/SharedArrayBuffer/DataView, Promise, Math, Date, JSON, RegExp, Reflect, Console, Symbol, Map/Set/WeakMap/WeakSet, BigInt, and async iteration helpers. Standard library coverage is expanding as more types move onto the generated constructor/prototype surface.

See the `tests/` tree and `docs/remaining-test262-gaps.md` for the most current implementation evidence.

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

Displays the legacy S-expression representation used by that demo.

---

## Building and Testing

```bash
# Canonical local quality gate
make quality
```

`make quality` runs `git diff --check`, builds the internal projects, then runs
the internal test suite without rebuilding. It intentionally excludes the
Test262 project from the default quality gate.

For ad hoc local checks, keep the build-before-test flow from
[`agents/how-to-build-and-test.md`](agents/how-to-build-and-test.md):

```bash
dotnet build
dotnet test tests/Asynkron.JsEngine.Tests
dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~SomeTestName"
```

---

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

See [LICENSE](LICENSE) file for details.

## Credits

Developed by Asynkron
