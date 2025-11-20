# `Asynkron.JsEngine` Context

This project implements a lightweight JavaScript-inspired execution engine. The current runtime parses source code into
a **typed abstract syntax tree (typed AST)** and executes it through a typed evaluator and generator IR pipeline. The
original S-expression (`Cons`-based) representation is still present in some internal APIs and docs for historical
reasons, but it no longer participates in the runtime execution path.

Key components:

- `Lexer` / `TypedAstParser` – Convert JavaScript source into a stream of tokens and then into a rich typed AST
  (`ProgramNode`, `StatementNode`, `ExpressionNode` and friends) with source locations.
- `TypedConstantExpressionTransformer` / `TypedCpsTransformer` – Optional transformation stages that fold constants
  and lower selected async/await constructs into Promise/CPS form before evaluation.
- `TypedAstEvaluator` – Executes the typed AST directly. It maintains lexical environments (`JsEnvironment`), closures,
  host interop via `IJsCallable`, and materialises object literals into prototype-aware `JsObject` instances with
  property access support. Method calls bind the object instance to `this`, and the `new` form wires constructor
  prototypes onto created objects while class declarations translate into constructor/prototype setups (including
  `extends` clauses that seed superclass chains and expose `super` inside constructors/methods).
- Generator IR – Synchronous generator functions are lowered into `GeneratorPlan`s and executed by a dedicated IR
  interpreter so `yield` / `yield*`, `try/finally`, and resumption via `.next/.throw/.return` behave consistently
  without replaying statements.
- Array literals lower into `JsArray` instances so indexed reads/writes, sparse growth, and `length` property lookups
  behave in a JavaScript-like manner alongside existing object/property support.
- Variable declarations cover `let`, `var`, and `const`; the evaluator hoists `var` bindings into the nearest
  function/global scope and blocks reassignment for `const` values.
- Control flow keywords such as `if`, `while`, `do/while`, `for`, `switch`, and `try/catch/finally` are represented
  by dedicated typed AST nodes so the evaluator can execute branching logic, handle loop-scoped variables, respect
  `break` / `continue` statements (including switch fallthrough and scoped breaks), and propagate exceptions via
  explicit `throw` forms.
- Logical operators (`&&`, `||`, `??`) short-circuit in the evaluator and surface their operand values, while equality
  operators cover both loose (`==`, `!=`) and strict (`===`, `!==`) comparisons.
- `JsObject` – Lightweight dictionary that tracks a prototype chain so property lookups can traverse prototypes.
- `JsEngine` – Public façade that exposes parsing and evaluation helpers, registers globals (Object, Array, Promise,
  Symbol, Map, Set, etc.), and integrates the event queue for timers and async behaviour.

Tests validating the behaviour live under `tests/Asynkron.JsEngine.Tests`.
