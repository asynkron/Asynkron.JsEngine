# Executing the typed AST

The typed AST exists so we can decouple syntax navigation from the cons-based
representation. To actually run code from this tree we have two broad options:

1. **Virtual dispatch on the nodes** – add an `Evaluate` method to `AstNode` and
   override it in every derived type.
2. **External interpreter** – keep the nodes as simple data records and drive
   execution from a dedicated evaluator that pattern matches on the node type.

We prefer the second option for a few reasons:

- The evaluator keeps execution policy separate from the data model. Nodes stay
  lightweight POCOs that are easy to serialize, inspect, or reuse in other
  passes (optimisers, emitters, etc.).
- Pattern matching (or visitors) makes control-flow explicit and tends to
  produce smaller diffs when we need to tweak semantics – no need to touch the
  node definitions themselves.
- The interpreter can carry auxiliary state (an `EvaluationContext`, stacks,
  caches) without overloading the AST with fields that only matter during
  execution.

To illustrate the approach we introduced `TypedAstEvaluator`. It now acts as the
canonical replacement for `ProgramEvaluator` and embodies the key ideas:

- An entry point that accepts a `ProgramNode` and a `JsEnvironment`.
- Statement/expression dispatch implemented via C# pattern matching so adding
  new node kinds is a matter of extending a single switch expression.
- A nested `TypedFunction` that implements `IJsCallable` using typed bodies. The
  implementation currently handles the simple parameter cases used by our
  recursion benchmarks, leaving hooks (and `NotSupportedException`s) for the
  remaining language surface.

With evaluation factored into a single class we can unit-test it in isolation and
evolve it without re-threading behaviour through the AST definitions. All runtime
semantics now live in the typed interpreter; the cons representation remains in
play strictly for parsing and transformation pipelines where its structural
simplicity is still valuable.

## Bootstrapping the runtime

`TypedProgramExecutor` now ships alongside the evaluator. It converts the
transformed S-expression into the typed tree and then always executes the typed
runtime. The legacy cons-based evaluator has been retired from the execution
path; cons cells are still produced by the parser so upstream transformations can
reuse the established tooling, but everything after the AST builder runs purely
on typed nodes. `TypedAstSupportAnalyzer` remains available as a diagnostic pass
for tooling or tests that want to assert coverage, yet its results no longer gate
runtime execution.
