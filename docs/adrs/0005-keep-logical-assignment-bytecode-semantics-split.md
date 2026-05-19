# ADR 0005: Keep logical assignment bytecode semantics split

## Status

Accepted

## Context

Issue #782 fixed the Test262 `Expressions_logicalAssignment` failure cluster.
The failures looked like one operator family, but they came from two different
ECMAScript semantics that the expression bytecode path had blurred together:

- logical assignment to an `IdentifierRef` can run NamedEvaluation for anonymous
  function, arrow, and class RHS values when the assignment branch executes;
- logical assignment to a member expression is a property write and must not use
  identifier-style name inference.

The same delivery also exposed an expression-position stack-shape issue for
member logical assignments. Short-circuiting has to leave the current property
value as the expression result while still cleaning the receiver/property-key
operands that were duplicated for the potential write. When the assignment
branch executes, the setter result must likewise leave a single expression
result and not leave receiver/key state behind.

## Decision

Keep identifier logical assignment, member property assignment, and
expression-result stack cleanup as separate bytecode concerns.

Identifier logical assignments may enable name inference only from the RHS
anonymous function/class shape and only when the source form is not the
parenthesized-assignment exclusion. Member and super assignments must pass
`AllowNameInference: false` through their `SetNamedProperty`,
`SetComputedProperty`, and super-property write opcodes, including logical
compound assignment branches.

For expression-position logical member assignments, compile both the write path
and the short-circuit path so they converge on the same stack contract: one
result value remains and duplicated receiver/property-key operands are removed.
Do not use a jump-around-write pattern that skips cleanup or leaves different
stack depths on each branch.

## Consequences

- Future logical-assignment work must prove identifier NamedEvaluation and member
  no-inference separately.
- Member assignment tests should cover strict getter-only and non-writable writes
  when the assignment branch runs, plus short-circuit cases where the strict
  write must be skipped.
- Expression bytecode reviews for `&&=`, `||=`, and `??=` should inspect stack
  depth on both the assignment and short-circuit paths before widening Test262
  proof runs.
