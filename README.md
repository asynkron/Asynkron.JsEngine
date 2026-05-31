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
- **[Current Test262 regression filters](tests/Asynkron.JsEngine.Tests.Test262/current-regressions.filter.txt)** - Active standards-compliance gap tracking and owning failure list.
- **[Regression packs](tests/Asynkron.JsEngine.Tests.Test262/regression-packs/)** - Themed slices derived from the umbrella filter for bounded work.
- **[Architecture decision records](docs/adrs/)** - Durable implementation decisions and quality-gate notes.
- **[Agent playbooks](agents/)** - Contributor/operator rules for coding standards, build/test commands, profiling, debugging, and workflow.

The older S-expression/CPS-era documents referenced by early README versions are no longer present in the repository. Use the current architecture docs and ADRs as the source of truth.

---

## Quick Overview

Asynkron.JsEngine targets ECMAScript 262 with full language coverage and a steadily growing built-in library.

### Current Status
- ECMAScript 262 language: broad core-language coverage with targeted remaining gaps tracked in the active Test262 regression filter.
- Built-ins / standard library: ~50% compliant; about half of the ES built-ins are implemented on the attribute-driven generator model and the rest are being migrated.

### Capabilities
- Variables, functions, classes, objects, arrays
- Async/await, Promises, generators
- ES modules (import/export) including dynamic imports
- Template literals, destructuring, spread/rest, operators, and control flow
- Implemented built-ins include Object, Array/TypedArray/ArrayBuffer/SharedArrayBuffer/DataView, Promise, Math, Date, JSON, RegExp, Reflect, Console, Symbol, Map/Set/WeakMap/WeakSet, BigInt, and async iteration helpers. Standard library coverage is expanding as more types move onto the generated constructor/prototype surface.

See the `tests/` tree, especially `tests/Asynkron.JsEngine.Tests.Test262/current-regressions.filter.txt` and `tests/Asynkron.JsEngine.Tests.Test262/regression-packs/`, for the most current implementation evidence.

---

## Running the Demo

Console application demos are included in the `examples` folder:

### Main Demo
```bash
rtk dotnet run --project examples/Demo
```

The main demo showcases basic features including variables, functions, closures, objects, arrays, control flow, operators, and standard library usage.

### Promise and Timer Demo
```bash
rtk dotnet run --project examples/PromiseDemo
```

Demonstrates setTimeout, setInterval, Promise creation, chaining, error handling, and event queue processing.

### Event Queue Demo
```bash
rtk dotnet run --project examples/EventQueueDemo
```

Shows host-scheduled task execution, nested scheduling, and async task queue draining through `JsEngine.ScheduleTask(...)`.

### NPM Package Compatibility Demo
```bash
rtk dotnet run --project examples/NpmPackageDemo
```

Shows that the engine can run pure JavaScript npm packages without Node.js dependencies.

### Node Host Demo
```bash
rtk dotnet run --project examples/NodeHostDemo
```

Runs the Node-shaped host sample that exposes a small `require(...)` module surface and can execute progressively larger CommonJS apps (including real npm frameworks) via the scripts documented in `examples/NodeHostDemo/README.md`.

---

## Building and Testing

Prerequisite: install a .NET 10 SDK before running repo build commands. The
engine, internal tests, and profiling tooling currently target `net10.0`.
After SDK setup, run `rtk make help` to list canonical entrypoints and
`rtk make quality` for the default local quality gate.

```bash
# Canonical local quality gate
rtk make quality
```

`make quality` runs `git diff --check`, builds the internal projects, then runs
the internal test suite without rebuilding. It intentionally excludes the
Test262 project from the default quality gate.

For bounded Test262 follow-up work, list available regression packs with
`rtk ./tools/run-test262-regressions.sh --list`, then run one named pack such
as `rtk ./tools/run-test262-regressions.sh temporal`. For the full runner
contract and filter-file modes, see
[`tests/Asynkron.JsEngine.Tests.Test262/README.md`](tests/Asynkron.JsEngine.Tests.Test262/README.md).

For ad hoc local checks, keep the build-before-test flow from
[`agents/how-to-build-and-test.md`](agents/how-to-build-and-test.md):

```bash
rtk dotnet build
rtk dotnet test tests/Asynkron.JsEngine.Tests
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~SomeTestName"
```

Recurring maintenance child runs should execute one bounded repo-local
maintenance slice (for example docs, tooling, test-fixture, or workflow
cleanup), check active sibling child summaries to avoid overlap, capture a
cheap baseline signal before editing and the matching final signal after
editing, and avoid adding recurrence infrastructure. Build updates should use
the stable evidence shape (`Baseline signal`, `Final signal`, `Sibling check`,
`Slice check`, `Scope note`) from
[`agents/how-to-build-and-test.md`](agents/how-to-build-and-test.md). Policy
ownership for recurring-child scope and durable compaction rules lives in
[`docs/rules/recurring-maintenance-child-runs.md`](docs/rules/recurring-maintenance-child-runs.md).

---

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

See [LICENSE](LICENSE) file for details.

## Credits

Developed by Asynkron
