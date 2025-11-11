# `Asynkron.JsEngine.Tests` Context

xUnit tests covering the JavaScript execution engine. The suite exercises:

- Lexing and parsing into `Cons`-based S-expressions.
- Evaluation of arithmetic, assignments (including property writes), expression statements, logical operators with
  short-circuit semantics, object literal/property behaviour, prototype chaining, array literals with indexed
  reads/writes (plus `length` semantics), the `new` constructor form, class declarations (including `extends`/`super`
  inheritance scenarios), `if` branches, `switch` clauses (with fallthrough and scoped `break` handling), loop
  constructs (`while`, `do/while`, `for`), exception handling via `try`/`catch`/`finally` and explicit `throw`, and
  `this` binding during method calls, including nested closures that capture receivers for later invocation.
- Variable declaration semantics for `let`, `var`, and `const`, covering function-scoped hoisting for `var` and
  reassignment guards for `const`.
- Function declarations, closures, and host interop registered through `JsEngine.SetGlobalFunction`.
