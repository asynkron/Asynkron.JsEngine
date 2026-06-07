# Level 2: Core Runtime Pipeline

The core runtime pipeline turns JavaScript source into executable engine state. The default path is parse, analyze/lower, then execute IR and expression payloads.

```mermaid
flowchart TB
  classDef compiler fill:#083344,stroke:#22d3ee,color:#ecfeff
  classDef core fill:#064e3b,stroke:#34d399,color:#ecfdf5,stroke-width:2px

  Source["JavaScript source"]:::compiler
  Parser["Parser<br/>lexer + JS AST parser"]:::compiler
  AST["Typed AST<br/>ProgramNode, statements, expressions"]:::compiler
  Analysis["AST analysis/cache<br/>hoisting, slots, shape checks"]:::compiler
  Builder["ExecutionPlanBuilder + Emitters<br/>statement lowering"]:::compiler
  IR["Statement IR<br/>ExecutionInstruction[]"]:::core
  Expr["ExpressionProgram<br/>PackedExpressionOp sequence"]:::core
  Runner["ExecutionPlanRunner<br/>dispatch loop + handlers"]:::core
  Bytecode["UnifiedBytecode<br/>eligible fast-path VM"]:::core

  Source --> Parser --> AST --> Analysis --> Builder
  Builder --> IR --> Runner
  Builder --> Expr --> Runner
  Expr -. "eligible payloads" .-> Bytecode --> Runner

  click Parser "level3-asynkron-jsengine.md" "Open Asynkron.JsEngine project architecture"
  click AST "level3-asynkron-jsengine.md" "Open Asynkron.JsEngine project architecture"
  click Analysis "level3-asynkron-jsengine.md" "Open Asynkron.JsEngine project architecture"
  click Builder "level3-asynkron-jsengine.md" "Open Asynkron.JsEngine project architecture"
  click IR "level3-asynkron-jsengine.md" "Open Asynkron.JsEngine project architecture"
  click Expr "level3-asynkron-jsengine.md" "Open Asynkron.JsEngine project architecture"
  click Runner "level3-asynkron-jsengine.md" "Open Asynkron.JsEngine project architecture"
  click Bytecode "level3-asynkron-jsengine.md" "Open Asynkron.JsEngine project architecture"
```

## Design

The parser creates a typed JavaScript AST. AST nodes are not just syntax containers; they also cache analysis products such as hoist plans, slot maps, execution plans, and shape information.

Lowering is split across `ExecutionPlanBuilder` and construct-specific emitters under `Execution/Emitters`. Emitters flatten statement control flow into `ExecutionInstruction` sequences and attach expression work as `ExpressionProgram` payloads.

`ExecutionPlanRunner` is the primary interpreter. It walks the instruction stream with a program counter and dispatches by instruction kind. Completion flow such as return, break, continue, throw, yield, and await is represented explicitly rather than by using normal exceptions as control flow.

`ExpressionProgram` is a stack-machine representation for expression execution. `UnifiedBytecode` is an additional compact VM path for eligible payloads and resumable shapes; unsupported or dynamic seams stay explicit.

## Boundaries

- Parser and AST code define source shape and analysis facts.
- Emitters own lowering decisions and should prefer normalization before runner-time fallbacks.
- Runner handlers own execution of already-lowered instructions.
- Unified bytecode eligibility must be proven narrowly before routing more shapes through the VM.

## Project Pages

- [Asynkron.JsEngine](level3-asynkron-jsengine.md)
