# Level 2: External Comparison and Test Packages

External packages provide comparison baselines and conformance infrastructure. They are dependencies of validation or performance tooling, not part of the engine runtime.

```mermaid
flowchart LR
  classDef tools fill:#713f12,stroke:#fbbf24,color:#fff7ed
  classDef tests fill:#831843,stroke:#f472b6,color:#fdf2f8
  classDef external fill:#1f2937,stroke:#94a3b8,color:#f8fafc

  Bench["Benchmarks"]:::tools
  Profile["ProfileRunner"]:::tools
  Test262Project["Tests.Test262"]:::tests

  Jint["Jint<br/>comparison engine"]:::external
  BDN["BenchmarkDotNet<br/>benchmark runner"]:::external
  Harness["Test262Harness<br/>ECMAScript test runner"]:::external

  Bench --> Jint
  Bench --> BDN
  Profile --> Jint
  Profile --> Harness
  Test262Project --> Harness

  click Bench "level3-asynkron-jsengine-benchmarks.md" "Open benchmark project architecture"
  click Profile "level3-profile-runner.md" "Open ProfileRunner architecture"
  click Test262Project "level3-asynkron-jsengine-tests-test262.md" "Open Test262 test project architecture"
```

## Design

`Jint` is used as a comparison engine in benchmark and profile workflows. It is useful for relative behavior and performance context, but it should not be treated as the specification.

`BenchmarkDotNet` owns benchmark execution, measurement, and reporting for the benchmark project.

`Test262Harness` owns integration with the ECMAScript Test262 corpus for the Test262 project and selected ProfileRunner probes.

These packages are intentionally kept outside the runtime path so that production engine behavior is not coupled to comparison or test infrastructure.

## Boundaries

- External comparison packages validate or measure the engine; they do not define engine architecture.
- Test262 results should be interpreted as conformance evidence.
- Jint comparisons should be interpreted with profiling data, not as a blanket architectural explanation.

## Project Pages

This module contains external packages, not repository projects. Project-level pages are attached to the validation and performance-tooling modules that consume them.
