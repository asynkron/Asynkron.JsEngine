# Level 1 Architecture

This is a high-level view of the main engine, runtime pipeline, tests, and performance tooling.

```mermaid
flowchart TB
  classDef core fill:#064e3b,stroke:#34d399,color:#ecfdf5,stroke-width:2px
  classDef compiler fill:#083344,stroke:#22d3ee,color:#ecfeff
  classDef support fill:#312e81,stroke:#a78bfa,color:#f5f3ff
  classDef tests fill:#831843,stroke:#f472b6,color:#fdf2f8
  classDef tools fill:#713f12,stroke:#fbbf24,color:#fff7ed
  classDef external fill:#1f2937,stroke:#94a3b8,color:#f8fafc

  Source["JavaScript source"]:::external

  subgraph Src["src"]
    Generator["Asynkron.JsEngine.Generators<br/>Roslyn analyzer"]:::compiler
    Engine["Asynkron.JsEngine<br/>public engine library"]:::core
  end

  Generator -. "analyzer reference" .-> Engine
  Source --> Engine

  subgraph Pipeline["Core runtime pipeline"]
    Parser["Parser<br/>lexer + JS AST parser"]:::compiler
    AST["Typed AST<br/>syntax + cached analysis"]:::compiler
    Lowering["ExecutionPlanBuilder + Emitters<br/>lower AST to execution plan"]:::compiler
    IR["Statement IR<br/>ExecutionInstruction[]"]:::core
    Expr["ExpressionProgram<br/>stack-machine expression ops"]:::core
    Runner["ExecutionPlanRunner<br/>IR interpreter"]:::core
    Bytecode["UnifiedBytecode<br/>eligible fast-path VM"]:::core
  end

  Engine --> Parser --> AST --> Lowering
  Lowering --> IR --> Runner
  Lowering --> Expr --> Runner
  Expr -. "eligible payloads" .-> Bytecode --> Runner

  subgraph Runtime["Runtime support"]
    Env["JsEnvironment<br/>scopes, slots, pools"]:::support
    Values["JsValue + JsTypes<br/>objects, arrays, promises, maps, typed arrays"]:::support
    StdLib["Runtime + StdLib<br/>ECMAScript operations and built-ins"]:::support
  end

  Runner <--> Env
  Runner <--> Values
  Runner <--> StdLib
  StdLib <--> Values
  Env <--> Values

  subgraph Validation["Validation"]
    UnitTests["Asynkron.JsEngine.Tests<br/>xUnit regression/spec tests"]:::tests
    Helpers["Asynkron.JsEngine.Tests.Helpers<br/>shared assertions/logging"]:::tests
    Test262["Asynkron.JsEngine.Tests.Test262<br/>NUnit + Test262Harness"]:::tests
  end

  UnitTests --> Engine
  UnitTests --> Helpers
  Helpers --> Engine
  Test262 --> Engine
  Test262 --> Helpers

  subgraph Perf["Performance tooling"]
    Benchmarks["Asynkron.JsEngine.Benchmarks<br/>BenchmarkDotNet comparisons"]:::tools
    ProfileRunner["ProfileRunner<br/>profile scripts + comparison probes"]:::tools
    Diagnostics["MemoryDiagnostic / AtomicsDebug<br/>PropertyEscapeProfile"]:::tools
  end

  Benchmarks --> Engine
  ProfileRunner --> Engine
  Diagnostics --> Engine

  subgraph External["External comparison/test packages"]
    Jint["Jint"]:::external
    BDN["BenchmarkDotNet"]:::external
    Harness["Test262Harness"]:::external
  end

  Benchmarks --> Jint
  Benchmarks --> BDN
  ProfileRunner --> Jint
  ProfileRunner --> Harness
  Test262 --> Harness
```
