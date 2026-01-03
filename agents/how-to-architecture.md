# Architecture Overview

- Parse to typed AST via `Lexer` + `JsAstParser` producing typed nodes (Program/Statement/Expression).
- Scope analysis stamps scope ids and slot metadata consumed by IR.
- IR-first execution: functions (sync/async/generator/async generator) and top-level scripts attempt IR via `ExecutionPlanBuilder` (`ScriptPlanCache` for scripts). `with` and direct `eval` stay on the AST walker.
- Unified runner: `ExecutionPlanRunner` executes `ExecutionPlan` streams with slot-based `JsEnvironment` layouts keyed by `LayoutId`/`RootScopeId`; handles per-iteration bindings, try/catch/finally, break/continue, yield/yield*, await.
- Generator prep: `GeneratorYieldLowerer` reshapes generator bodies; async/await emitted directly into IR instructions.
- Fallback: IR build failures or `NotSupportedException` fall back to `TypedAstEvaluator` AST walking for that function or script.
