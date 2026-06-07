# Level 3: Asynkron.JsEngine.Tests.Helpers

Parent module: [Validation](level2-validation.md)

`Asynkron.JsEngine.Tests.Helpers` contains shared test infrastructure used by focused tests and Test262-derived tests.

```mermaid
flowchart LR
  classDef tests fill:#831843,stroke:#f472b6,color:#fdf2f8
  classDef core fill:#064e3b,stroke:#34d399,color:#ecfdf5,stroke-width:2px

  Factory["TestEngineFactory<br/>debug engine setup"]:::tests
  AstHelpers["AstTestHelpers<br/>parse/analyze/traverse"]:::tests
  Logger["TestLogger<br/>realm/debug logging"]:::tests
  Engine["Asynkron.JsEngine"]:::core
  Consumers["unit + Test262 tests"]:::tests

  Factory --> Engine
  AstHelpers --> Engine
  Logger --> Engine
  Consumers --> Factory
  Consumers --> AstHelpers
  Consumers --> Logger
```

## Design

The helpers project keeps common setup out of individual test files. It provides configured engine construction, optional debug/realm logging, and AST parsing/traversal utilities for tests that need to inspect compiler state directly.

`AstTestHelpers` runs the lexer/parser/constant-folding path used by structural tests and exposes traversal helpers that remain resilient as AST node types evolve.

## Boundaries

- Helpers should stay test-only and avoid production behavior.
- Put reusable setup here only when multiple test projects or many test files use it.
- Keep assertion helpers small enough that test failures still point at the behavior being proven.
