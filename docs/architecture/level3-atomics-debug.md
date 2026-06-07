# Level 3: AtomicsDebug

Parent module: [Performance Tooling](level2-performance-tooling.md)

`AtomicsDebug` is a small executable for isolating Atomics and IR diagnostic behavior.

```mermaid
flowchart LR
  classDef tools fill:#713f12,stroke:#fbbf24,color:#fff7ed
  classDef core fill:#064e3b,stroke:#34d399,color:#ecfdf5,stroke-width:2px

  Program["Program.cs<br/>debug scenario"]:::tools
  Engine["JsEngine<br/>AllowScriptSlotAnalysis"]:::core
  Diagnostics["ExecutionPlanDiagnostics<br/>accepted/succeeded/failed counts"]:::tools
  Scenario["strict and non-strict for-in scripts"]:::tools

  Program --> Engine
  Program --> Scenario --> Engine
  Engine --> Diagnostics
```

## Design

Despite the project name, the current executable is a narrow IR diagnostic probe. It enables script slot analysis, runs strict and non-strict `for-in` scripts, and prints `ExecutionPlanDiagnostics` counters.

## Boundaries

- Keep this project scoped to one-off low-friction debugging.
- If a scenario becomes durable regression coverage, move it into the test suite.
- If a scenario becomes a recurring performance probe, move it into ProfileRunner or benchmarks.
