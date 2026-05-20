# ADR 0043: Keep top-level await try class declarations on await-temp path

## Status

Accepted

## Context

Issue #825 / PR #1120 fixed the Test262 `Statements_class` cases where a
top-level-await module evaluated a class declaration inside `try/catch` and the
class had awaited computed method or accessor names.

The runtime already had a class-declaration await-temp path for async functions
and top-level-await module bodies. That path rewrites awaited computed class
element names into resumable temporaries, then continues class definition
evaluation after the awaited values settle. The failing shape bypassed that
owner path because `AsyncModuleBodyRunner.ExecuteStatementWithAwaitInTry`
handled `try` body statements through a narrower dispatch table. A
`ClassDeclaration` containing awaited computed names therefore fell through to
the unsupported nested-await statement path instead of using the existing class
await-temp bridge.

The fix added class-declaration dispatch inside the module `try/catch` await
runner and proved the behavior with module regressions for computed methods and
accessors, async-function class declaration regressions, the
`Name=Statements_class` Test262 method group, focused internal tests,
`git diff --check`, and the AST-eval seam scan.

## Decision

Top-level-await `try/catch` statement execution must route class declarations
with awaited computed member names through the shared class-declaration
await-temp path.

Do not repair this shape by adding a generic AST fallback for class declarations
inside module `try/catch`, by duplicating computed-name evaluation in the
try-runner, or by treating awaited class elements as a plain expression-statement
case. The class declaration owner path must continue to handle method names,
accessor names, static elements, binding initialization, and continuation
resumption as one class-definition operation.

When a bridge or specialized TLA runner grows support for more statement
shapes, it must check whether an existing await-temp or lowering owner already
defines that syntax's evaluation order. If so, route to that owner instead of
adding a local runner special case.

## Consequences

- Future TLA `try/catch` fixes must inspect statement dispatch and the existing
  await-temp/lowering owner before adding unsupported-statement fallbacks.
- Regression coverage for this class needs both module `try/catch` behavior and
  ordinary async-function behavior so the shared class path stays shared.
- Computed class method and accessor names, including static elements, should
  remain in the same proof pack because they exercise the same awaited
  class-name continuation machinery.
- The focused proof for this class should include local module regressions,
  local async-function class declaration regressions, the owning Test262 method
  group, and the AST-eval seam scan.
