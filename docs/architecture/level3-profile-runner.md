# Level 3: ProfileRunner

Parent module: [Performance Tooling](level2-performance-tooling.md)

`ProfileRunner` is the script-oriented profiling and comparison harness used by repository profiling commands.

```mermaid
flowchart TB
  classDef tools fill:#713f12,stroke:#fbbf24,color:#fff7ed
  classDef core fill:#064e3b,stroke:#34d399,color:#ecfdf5,stroke-width:2px
  classDef external fill:#1f2937,stroke:#94a3b8,color:#f8fafc

  CLI["Program.cs<br/>profile CLI/options"]:::tools
  Manifest["tools/profile-manifest.json"]:::tools
  Scripts["tools/profile-scripts/*.js"]:::tools
  AsynkronRun["Asynkron execution path"]:::tools
  JintRun["Jint execution path"]:::tools
  Reports["timing, allocation, storage, route-hit reports"]:::tools
  Engine["Asynkron.JsEngine"]:::core
  Jint["Jint"]:::external
  Harness["Test262Harness"]:::external

  CLI --> Manifest --> Scripts
  CLI --> AsynkronRun --> Engine
  CLI --> JintRun --> Jint
  CLI --> Harness
  AsynkronRun --> Reports
  JintRun --> Reports
```

## Design

`ProfileRunner` loads named profiles from `tools/profile-manifest.json`, runs the associated JavaScript workload, and reports timing or diagnostic data.

It can execute through Asynkron or Jint, measure allocations, report expression-program or statement-instruction storage, and collect route-hit information for production fast paths.

Repository wrapper scripts such as `tools/profile` and `benchmark.sh` use this project for fast iteration before or alongside heavier BenchmarkDotNet runs.

## Boundaries

- Use ProfileRunner for quick profiling loops and route visibility.
- Keep profile scripts in the manifest/script corpus rather than embedding large workloads in code.
- Treat output as diagnostic evidence; confirm stable benchmark claims separately when needed.
