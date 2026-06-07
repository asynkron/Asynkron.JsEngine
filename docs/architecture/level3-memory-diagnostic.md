# Level 3: MemoryDiagnostic

Parent module: [Performance Tooling](level2-performance-tooling.md)

`MemoryDiagnostic` is a standalone diagnostic executable for coarse retained-memory and allocation-shape investigations.

```mermaid
flowchart LR
  classDef tools fill:#713f12,stroke:#fbbf24,color:#fff7ed
  classDef core fill:#064e3b,stroke:#34d399,color:#ecfdf5,stroke-width:2px

  Program["Program.cs<br/>scenario sequence"]:::tools
  Engine["JsEngine"]:::core
  Scripts["targeted JS scenarios<br/>regex, Temporal, property escapes"]:::tools
  GC["forced GC checkpoints<br/>memory deltas"]:::tools

  Program --> Engine
  Program --> Scripts --> Engine
  Program --> GC
```

## Design

The tool constructs an engine, runs targeted JavaScript scenarios, forces GC between checkpoints, and prints memory deltas. Current scenarios include trivial evaluation, RegExp, unicode property escapes, Temporal, and compiled regex stress.

## Boundaries

- Use this tool for coarse memory decomposition, not as a formal benchmark gate.
- Keep scenarios explicit and tied to one memory question.
- Confirm performance regressions with benchmark/profile tooling when needed.
