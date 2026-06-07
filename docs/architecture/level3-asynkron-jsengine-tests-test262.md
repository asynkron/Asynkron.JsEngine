# Level 3: Asynkron.JsEngine.Tests.Test262

Parent module: [Validation](level2-validation.md)

`Asynkron.JsEngine.Tests.Test262` integrates the ECMAScript Test262 corpus through NUnit and `Test262Harness`.

```mermaid
flowchart TB
  classDef tests fill:#831843,stroke:#f472b6,color:#fdf2f8
  classDef core fill:#064e3b,stroke:#34d399,color:#ecfdf5,stroke-width:2px
  classDef external fill:#1f2937,stroke:#94a3b8,color:#f8fafc

  Runsettings["Language/BuiltIns runsettings"]:::tests
  HarnessState["TestHarness + State<br/>harness files and sources"]:::tests
  Regression["Regression/focused Test262 tests"]:::tests
  Cache["Test262SuiteDiskCache"]:::tests
  AgentRuntime["Test262AgentRuntime"]:::tests
  Engine["Asynkron.JsEngine"]:::core
  Harness["Test262Harness"]:::external

  Runsettings --> Harness
  HarnessState --> Harness
  Cache --> Harness
  AgentRuntime --> Harness
  Regression --> Engine
  Harness --> Engine
```

## Design

This project is the broad standards-conformance surface. It runs large Test262 packs via runsettings files and also contains focused tests for known edge cases and regressions.

`TestHarness` initializes custom harness state by loading harness files into `State.Sources`. Disk-cache and agent-runtime support keep large conformance runs practical.

Focused Test262-derived tests live beside broad harness integration so failing cases can be reproduced without always running the full corpus.

## Boundaries

- Use this project for ECMAScript conformance evidence.
- Keep exact failing file filters narrow during triage.
- Do not use Test262 failures as a substitute for a focused unit regression when a bug is fixed.
