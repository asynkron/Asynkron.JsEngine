# Level 3: Asynkron.JsEngine

Parent modules: [src](level2-src.md), [Core Runtime Pipeline](level2-core-runtime-pipeline.md), [Runtime Support](level2-runtime-support.md)

`Asynkron.JsEngine` is the public runtime library. It owns the host-facing engine API, parsing/lowering/execution pipeline, JavaScript value model, runtime state, and standard library.

```mermaid
flowchart TB
  classDef api fill:#064e3b,stroke:#34d399,color:#ecfdf5,stroke-width:2px
  classDef compiler fill:#083344,stroke:#22d3ee,color:#ecfeff
  classDef runtime fill:#312e81,stroke:#a78bfa,color:#f5f3ff

  Host["Host application"]:::api
  Engine["JsEngine<br/>Evaluate / module/event-loop facade"]:::api
  Parser["Parser<br/>lexer + AST parser"]:::compiler
  Ast["Ast<br/>typed nodes + analysis caches"]:::compiler
  Execution["Execution<br/>plans, emitters, instructions"]:::compiler
  Runner["ExecutionPlanRunner<br/>IR and expression execution"]:::api
  Values["JsTypes / JsValue<br/>JS values and objects"]:::runtime
  Runtime["Runtime<br/>ECMAScript operations + realm state"]:::runtime
  StdLib["StdLib<br/>built-ins and prototypes"]:::runtime

  Host --> Engine
  Engine --> Parser --> Ast --> Execution --> Runner
  Runner <--> Values
  Runner <--> Runtime
  Runtime <--> StdLib
  StdLib <--> Values
  Engine <--> Runtime
```

## Design

`JsEngine` is the high-level facade. It constructs realm/global state, installs standard library objects, exposes evaluation APIs, manages module state, and coordinates event-loop/microtask behavior.

The parser and typed AST are internal compiler front-end layers. AST nodes carry both syntax and cached analysis products, so repeated evaluation can reuse hoist plans, slot maps, and execution plans.

The execution layer lowers statements into instruction plans and expression payloads. `ExecutionPlanRunner` interprets lowered instructions and delegates JavaScript value semantics to `JsTypes`, `Runtime`, and `StdLib`.

## Boundaries

- Public host API belongs on or near `JsEngine`.
- JavaScript semantics belong in `Runtime`, `StdLib`, or the relevant `JsTypes` type.
- Lowering belongs in `Execution` emitters and builders.
- Hot paths should avoid boxing, avoid culture-sensitive conversions, and prefer slot-based state.
