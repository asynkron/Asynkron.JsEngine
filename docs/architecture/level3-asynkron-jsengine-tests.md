# Level 3: Asynkron.JsEngine.Tests

Parent module: [Validation](level2-validation.md)

`Asynkron.JsEngine.Tests` is the focused xUnit test project for engine behavior, regressions, and internal invariants.

```mermaid
flowchart TB
  classDef tests fill:#831843,stroke:#f472b6,color:#fdf2f8
  classDef core fill:#064e3b,stroke:#34d399,color:#ecfdf5,stroke-width:2px

  Fixture["JsEngineTestFixture<br/>engine/options setup"]:::tests
  TestBase["InternalTestBase / helpers<br/>shared assertions"]:::tests
  Focused["focused regression tests<br/>parser, scope, stdlib, async"]:::tests
  Bytecode["bytecode/IR proof packs<br/>routing and resumable cases"]:::tests
  Engine["Asynkron.JsEngine"]:::core
  Shared["Tests.Helpers"]:::tests

  Fixture --> Engine
  TestBase --> Engine
  Focused --> Fixture
  Focused --> Shared
  Bytecode --> Engine
  Bytecode --> Shared
```

## Design

This project uses narrow tests for specific engine features and regressions. It covers parser behavior, scope/slot analysis, async and generator behavior, built-ins, optional chaining, private fields, completion values, and production bytecode routing.

Many files are issue- or feature-focused proof packs. That structure keeps regressions easy to localize and lets optimization work prove one seam at a time.

`JsEngineTestFixture` centralizes normal engine construction and option handling for tests that need shared setup.

## Boundaries

- Use this project for focused proofs and internal engine invariants.
- Keep failing-case filters narrow before expanding coverage.
- Shared reusable setup belongs in `Asynkron.JsEngine.Tests.Helpers`.
