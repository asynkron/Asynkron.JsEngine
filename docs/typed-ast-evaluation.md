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

To illustrate the approach we introduced `TypedAstEvaluator`. It is **not** a
complete replacement for `JsEvaluator` yet, but it demonstrates the key ideas:

- An entry point that accepts a `ProgramNode` and a `JsEnvironment`.
- Statement/expression dispatch implemented via C# pattern matching so adding
  new node kinds is a matter of extending a single switch expression.
- A nested `TypedFunction` that implements `IJsCallable` using typed bodies. The
  implementation currently handles the simple parameter cases used by our
  recursion benchmarks, leaving hooks (and `NotSupportedException`s) for the
  remaining language surface.

As the interpreter matures we can gradually widen the supported node set and
port semantics from `JsEvaluator` into the typed version. Because evaluation is
now factored into a single class, we can unit-test it in isolation and evolve it
without re-threading behaviour through the AST definitions.

## Recent improvements

`TypedAstEvaluator` now understands a much larger portion of the surface area:

- Property reads/writes (including optional chaining) and `new` construction now
  use the same helper routines as the legacy evaluator, so object literals and
  method calls behave as expected.
- Control-flow constructs such as `while`, `do/while`, and classic `for` loops
  execute natively, including support for labeled `break`/`continue`.
- Array/object literals (with spreads), template literals, and tagged templates
  are fully evaluated.

These changes make it viable to execute real programs purely through the typed
pipeline while keeping a small compatibility layer for helpers that still live
in `JsEvaluator`.
