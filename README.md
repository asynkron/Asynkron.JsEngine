<img src="assets/images/logo.png" width="100%" alt="Asynkron.JsEngine Logo">

# Asynkron.JsEngine

Asynkron.JsEngine is an embeddable ECMAScript engine for .NET. The current
project is a standards, runtime, and performance effort around a typed-AST
pipeline, statement IR, expression bytecode, and a production unified-bytecode
VM.

The short version: this is now a multi-tier JavaScript engine that can execute
broad ECMAScript language shapes, drive promises and async work through an event
loop, host ES modules, run real JavaScript package code through .NET-provided
host APIs, and route a growing production surface through unified bytecode.

The work is still in flight. The engine is not claiming full Node.js parity,
full Test262 conformance, or full bytecode-only execution yet. Those boundaries
are tracked explicitly in the roadmap, bytecode progress map, Test262
regression filters, and ADRs linked below.

## What This Project Is

Asynkron.JsEngine is built for .NET hosts that need to parse, execute, and
embed JavaScript without shelling out to Node.js. The public surface centers on
`JsEngine`, which supports synchronous evaluation for pure sync scripts,
event-loop-backed evaluation for promises/timers/async work, module evaluation,
host functions, and host-provided module loading.

Internally, execution is deliberately not a single tree-walker:

```text
JavaScript source
  -> parser and typed AST
  -> analysis, lowering, and cached execution plans
  -> one of the current execution tiers:
       - UnifiedBytecodeVirtualMachine for accepted production bytecode shapes
       - ExpressionProgram VM for expression-bytecode payloads
       - ExecutionPlanRunner for lowered statement IR
       - quarantined dynamic / legacy AST bridge for correctness fallback
```

Accepted production unified-bytecode programs execute all-or-nothing in
`UnifiedBytecodeVirtualMachine`; they must not dip back into expression
bytecode, the statement IR runner, or AST evaluation. Anything outside the
current admitted boundary still falls back to the older tiers. That is the
current reality and the migration path.

## Where We Are Now

Current status, based on the maintained June 2026 roadmap and bytecode progress
documents:

- The engine targets `net10.0` and ECMAScript 262 behavior.
- Core language coverage is broad: variables, functions, closures, classes,
  private names, objects, arrays, control flow, destructuring, spread/rest,
  template literals, modules, promises, async/await, generators, and async
  generators all have substantial implementation coverage.
- The standard library is large but still being completed and hardened. Current
  implemented surfaces include Object, Array, TypedArray, ArrayBuffer,
  SharedArrayBuffer, DataView, Promise, Math, Date, JSON, RegExp, Reflect,
  Console, Symbol, Map/Set/WeakMap/WeakSet, BigInt, async iteration helpers,
  and growing Intl/Temporal-related pieces.
- The unified-bytecode VM is real production infrastructure, not a prototype
  diagram. The progress map currently lists a 131-opcode unified program
  surface, a 77-op expression bytecode VM, and a 43-kind statement IR runner.
- Ordinary sync functions that pass the production gates attempt the unified VM
  before the old simple IR shortcuts and generic IR fallback. That is selector
  coverage for accepted shapes, not a claim that every function shape is
  accepted.
- Resumable bytecode is narrower but no longer theoretical. Generators,
  selected async/generator bodies, direct and awaited-source `yield*`, selected
  resumable calls, property operations, literals, optional chains, and dynamic
  identifier reads/calls have focused route proof.
- Top-level scripts have a production route for admitted shapes, including
  narrow property-access and simple arithmetic profiles. Non-admitted script,
  module, abrupt-completion, and dynamic-script shapes still use classified
  fallback paths.
- Test262 work is active and evidence-driven. The current regression filter has
  523 entries, with named packs for `language`, `regexp`, `intl`, `temporal`,
  `proxy`, `annexb`, array-prototype work, and issue-specific slices.
- The Node-shaped host demo can run progressively larger CommonJS/package
  workloads, including real Express and Polka examples through host-provided
  `require`, `http`, `fs`, path, package resolution, and request/response
  surfaces. This demonstrates host compatibility work; it is not a claim that
  the core engine is a Node.js clone.

## Current Boundaries

The remaining work is mostly semantic admission and fallback retirement, not
"add missing switch arms" in the VM. The maintained bytecode contract says the
declared unified opcodes and VM switch are expected to stay in lockstep.

Important open boundaries include:

- broader activation semantics: real `arguments` object behavior, destructured
  and runtime-dependent parameters, lexical-this arrows outside admitted routes,
  and broader async/generator activation;
- wider call invocation: complex receivers, complex computed keys,
  eval-sensitive calls, private-adjacent targets, and receiver-binding-sensitive
  families;
- dynamic lookup and dynamic activation residue, including retained live `with`
  scopes and harder direct-eval / generated-function bodies;
- property and assignment neighbors around optional, super, private, richer
  computed-key, and unsupported RHS spans;
- destructuring defaults, nested patterns, parameter destructuring, generic
  declarations, and disposal-aware binding targets;
- class-definition state, static-block ownership, class declaration/expression
  environment bridges, and remaining computed/private/static neighbors;
- retirement of the remaining `ExpressionProgram` and `ExecutionPlanRunner`
  hot paths after A/B/C parity is proven with source gates and focused tests.

## Documentation

### Getting Started
- Build and test commands are in [Building and Testing](#building-and-testing).
- Examples live under [`examples/`](examples/), including the basic console
  demo, promise and event queue demos, npm package and host demos, and the
  Avalonia SVG browser/presentation sample.

### Architecture and Design
- **[Current architecture deep dive](docs/architecture-current-deep-dive-2026-05-13.md)** - Current execution architecture, IR boundaries, and implementation notes.
- **[Roadmap](docs/roadmap.md)** - Current project direction, landed work, open runtime boundaries, and long-term targets.
- **[Bytecode progress map](docs/bytecode-progress.md)** - Compact overview of unified-bytecode coverage, route-hit evidence, and remaining red buckets.
- **[Unified bytecode expansion contract](docs/unified-bytecode-expansion-contract.md)** - Source-of-truth contract for unified-bytecode boundaries, accepted routes, and decline inventories.
- **[Architecture snapshot](docs/architecture-current-2026-05-13.html)** - Rendered architecture reference.
- **[Architecture first-code snapshot](docs/architecture-first-code-2025-11-07.html)** - Earlier architecture artifact kept for historical comparison.

### Status and Decisions
- **[Current Test262 regression filters](tests/Asynkron.JsEngine.Tests.Test262/current-regressions.filter.txt)** - Active standards-compliance gap tracking and owning failure list.
- **[Regression packs](tests/Asynkron.JsEngine.Tests.Test262/regression-packs/)** - Themed slices derived from the umbrella filter for bounded work.
- **[Architecture decision records](docs/adrs/)** - Durable implementation decisions and quality-gate notes.
- **[Agent playbooks](agents/)** - Contributor/operator rules for coding standards, build/test commands, profiling, debugging, and workflow.

Older parser-transform documents referenced by early README versions are no
longer present in the repository. Use the current architecture docs, roadmap,
bytecode contract, Test262 filters, and ADRs as the source of truth.

---

## Minimal Usage

```csharp
using Asynkron.JsEngine;

await using var engine = new JsEngine();

var value = await engine.Evaluate("""
    const values = [1, 2, 3, 4];
    values.map(x => x * 2).reduce((a, b) => a + b, 0);
""");

Console.WriteLine(value); // 20
```

Use `EvaluateSync` only for code that does not depend on promises, timers,
async/await, or the event loop. Use `Evaluate` or `EvaluateModule` when the
script needs asynchronous behavior.

## Running Demos

Example projects are included in the `examples` folder:

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

Shows that the engine can run pure JavaScript npm packages without Node.js
dependencies.

### Node Host Demo
```bash
rtk dotnet run --project examples/NodeHostDemo
```

Runs the Node-shaped host sample that exposes a small `require(...)` module
surface and can execute progressively larger CommonJS apps, including real npm
frameworks, via the scripts documented in
[`examples/NodeHostDemo/README.md`](examples/NodeHostDemo/README.md).

### Avalonia SVG Browser Demo
```bash
rtk dotnet run --project examples/AvaloniaSvgBrowserDemo
```

Runs the Avalonia desktop SVG browser and presentation sample. The project also
includes presentation smoke modes used by repo-local checks.

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

## Profiling and Benchmarks

The stable timing comparison entrypoint is:

```bash
rtk ./benchmark.sh
```

For managed allocation comparison against Jint:

```bash
rtk ./benchmark.sh --allocations
```

For route-hit evidence showing where the production unified-bytecode route is
actually entered:

```bash
rtk ./tools/profile forloop --route-hits
```

For deeper CPU or allocation analysis:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
rtk ./tools/profile classdef --memory --calltree-depth 40 --calltree-width 40
```

Recurring maintenance child runs should execute one bounded repo-local
maintenance slice (for example docs, tooling, test-fixture, or workflow
cleanup), check active sibling child summaries to avoid overlap, capture a
cheap baseline signal before editing and the matching final signal after
editing, and avoid adding recurrence infrastructure. Build updates should use
the stable evidence shape (`Baseline timestamp`, `Baseline signal`,
`Final timestamp`, `Final signal`, `Signal delta`, `Sibling check`,
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
