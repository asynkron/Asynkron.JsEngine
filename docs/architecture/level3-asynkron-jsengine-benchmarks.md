# Level 3: Asynkron.JsEngine.Benchmarks

Parent module: [Performance Tooling](level2-performance-tooling.md)

`Asynkron.JsEngine.Benchmarks` is the BenchmarkDotNet project for stable measurement suites.

```mermaid
flowchart TB
  classDef tools fill:#713f12,stroke:#fbbf24,color:#fff7ed
  classDef core fill:#064e3b,stroke:#34d399,color:#ecfdf5,stroke-width:2px
  classDef external fill:#1f2937,stroke:#94a3b8,color:#f8fafc

  Program["Program.cs<br/>benchmark selection"]:::tools
  Suites["Benchmark suites<br/>lexer, parser, evaluator, pipeline"]:::tools
  Ops["operation/fast-path/overhead suites"]:::tools
  JintSuite["Jint comparison suites"]:::tools
  Engine["Asynkron.JsEngine"]:::core
  BDN["BenchmarkDotNet"]:::external
  Jint["Jint / Esprima"]:::external

  Program --> Suites
  Program --> Ops
  Program --> JintSuite
  Suites --> Engine
  Ops --> Engine
  JintSuite --> Engine
  JintSuite --> Jint
  Program --> BDN
```

## Design

The project exposes benchmark categories through `Program.cs`: lexer, parser, evaluator, fast paths, pipeline, operations, overhead, Jint execution comparison, and Jint/Esprima parser comparison.

BenchmarkDotNet owns measurement, memory diagnoser output, ordering, statistics, and JSON export. Jint and Esprima are comparison dependencies only.

## Boundaries

- Use this project for stable benchmark numbers.
- Keep benchmarks deterministic and representative of engine-owned work.
- Do not route ad hoc profiling behavior here when `ProfileRunner` is a better fit.
