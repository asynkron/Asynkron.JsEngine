# Level 3: PropertyEscapeProfile

Parent module: [Performance Tooling](level2-performance-tooling.md)

`PropertyEscapeProfile` is a standalone profiling executable for RegExp unicode property escape behavior.

```mermaid
flowchart TB
  classDef tools fill:#713f12,stroke:#fbbf24,color:#fff7ed
  classDef core fill:#064e3b,stroke:#34d399,color:#ecfdf5,stroke-width:2px

  Harness["embedded Test262-style harness<br/>buildString + testPropertyEscapes"]:::tools
  Profiles["property profiles<br/>script, binary, negated forms"]:::tools
  Engine["JsEngine"]:::core
  Timing["Stopwatch timings<br/>warm-up and scenario output"]:::tools

  Harness --> Engine
  Profiles --> Engine
  Engine --> Timing
```

## Design

The tool embeds a reduced Test262-style RegExp property escape harness, warms unicode data, and runs representative property profiles through `JsEngine` while reporting timings.

It is intentionally more targeted than `MemoryDiagnostic` and more ad hoc than the benchmark suite.

## Boundaries

- Use this project for unicode property escape profiling.
- Move durable correctness cases into Test262 or focused unit tests.
- Move stable throughput measurements into BenchmarkDotNet or ProfileRunner.
