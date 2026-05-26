# ADR 0150: Keep simple arrow IR activation lexical-dependency guarded

## Status

Accepted

## Context

Issue `autrun-dis8iqooxge8-6666a730d6` / PR #1985 selected `classdef` from the
performance automation baseline. The baseline showed Asynkron at 1414 ms while
Jint was at 301 ms. A focused CPU profile showed the final
`dogs.map(d => d.speak())` callback entering `SyncFunctionInvoker` and falling
through full invocation even though the callback was a simple one-parameter
arrow with a simple return expression.

Earlier `classdef` slices had already narrowed array callback argument carriers
for simple arrows. This slice found the next activation boundary: all arrow
functions were still rejected by the simple IR activation fast path because
arrows can inherit lexical `this`, lexical `new.target`, and `super` semantics
from the enclosing function or class body.

The accepted change did not make arrows generally activation-fast. It allowed
only simple-arrow return programs whose expression bytecode has no lexical
`this`, `new.target`, or `super` operation. Those dependency-bearing arrows
continue through the full invocation path that already preserves arrow lexical
binding behavior.

## Decision

Keep simple arrow IR activation eligible only when the already-lowered simple
return `ExpressionProgram` proves the arrow does not need arrow-specific
lexical binding semantics.

The eligibility boundary is:

1. the existing simple IR activation base and activation-slot shape checks must
   still pass;
2. the function must still reject async, generator, parameter-expression,
   `arguments`, captured-activation, dynamic identifier-cache, private-name,
   `super`, class-field, and home-object shapes already guarded by the invoker;
3. arrow functions additionally need a simple return program; and
4. that program must contain no `ExpressionOpKind` that loads lexical `this`,
   loads lexical `new.target`, accesses or updates `super`, or constructs via
   `super`.

Do not replace this with a source-level arrow predicate or callback-length
heuristic. The lowered expression program is the durable proof surface because
it reflects the executable bytecode payload that the fast path will evaluate.

Do not move dependency-bearing arrows onto the simple activation path just
because they look syntactically small. A one-parameter arrow that reads
`this.factor`, `new.target`, or `super.method()` is semantically different from
`d => d.speak()` and must keep the full arrow invocation semantics until a
separate proof models those dependencies in the fast path.

## Consequences

- Simple array callback arrows can reuse the simple IR activation path when the
  bytecode proves no arrow lexical binding state is needed.
- Arrow dependency checks stay close to the expression bytecode operation set,
  so adding new lexical-this/new-target/super op kinds requires updating the
  eligibility scan.
- Future activation work touching this boundary should pair selected-profile
  evidence with focused array-callback semantics tests, negative lexical
  `this` / `new.target` / `super` fallback coverage, the AST-eval seam scan,
  and `forloop --memory`.
- The performance note
  `docs/performance/classdef-arrow-simple-ir-activation.md` remains the
  measurement evidence; this ADR records the semantic policy.

## Related

- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`
- `docs/adrs/0124-keep-lazy-arguments-object-materialization-observable-and-profile-owned.md`
- `docs/performance/classdef-arrow-simple-ir-activation.md`
