# Level 2: Validation

The validation module proves engine behavior at three levels: focused unit/regression tests, shared test helpers, and Test262 conformance coverage.

```mermaid
flowchart TB
  classDef tests fill:#831843,stroke:#f472b6,color:#fdf2f8
  classDef core fill:#064e3b,stroke:#34d399,color:#ecfdf5,stroke-width:2px
  classDef external fill:#1f2937,stroke:#94a3b8,color:#f8fafc

  Engine["Asynkron.JsEngine"]:::core
  Helpers["Asynkron.JsEngine.Tests.Helpers<br/>factory, AST helpers, test logger"]:::tests
  Unit["Asynkron.JsEngine.Tests<br/>xUnit focused regression packs"]:::tests
  Test262["Asynkron.JsEngine.Tests.Test262<br/>NUnit conformance harness"]:::tests
  Harness["Test262Harness"]:::external

  Unit --> Engine
  Unit --> Helpers
  Helpers --> Engine
  Test262 --> Engine
  Test262 --> Helpers
  Test262 --> Harness

  click Engine "level3-asynkron-jsengine.md" "Open Asynkron.JsEngine architecture"
  click Helpers "level3-asynkron-jsengine-tests-helpers.md" "Open test helpers architecture"
  click Unit "level3-asynkron-jsengine-tests.md" "Open focused test project architecture"
  click Test262 "level3-asynkron-jsengine-tests-test262.md" "Open Test262 test project architecture"
  click Harness "level2-external-comparison-test-packages.md" "Open external package architecture"
```

## Design

`Asynkron.JsEngine.Tests` is the focused regression and behavior suite. It contains narrow tests for parser behavior, scope/slot semantics, async/generator behavior, built-ins, optional chaining, unified bytecode routing, completion values, and many issue-specific proof packs.

`Asynkron.JsEngine.Tests.Helpers` centralizes shared test setup: engine factory helpers, AST helpers, and logger support. This keeps focused tests short and prevents repeated harness setup.

`Asynkron.JsEngine.Tests.Test262` runs ECMAScript conformance tests through `Test262Harness`. It also contains focused Test262-derived regression tests, disk cache support, agent runtime support, and settings/runsettings files for large language and built-in packs.

## Boundaries

- Use focused xUnit tests for local bug proofs and internal invariants.
- Use Test262 for standards conformance and broad compatibility checks.
- Shared setup belongs in the helpers project when multiple test suites need it.
- Exact failing filters should stay narrow before widening into large Test262 packs.

## Project Pages

- [Asynkron.JsEngine.Tests](level3-asynkron-jsengine-tests.md)
- [Asynkron.JsEngine.Tests.Helpers](level3-asynkron-jsengine-tests-helpers.md)
- [Asynkron.JsEngine.Tests.Test262](level3-asynkron-jsengine-tests-test262.md)
