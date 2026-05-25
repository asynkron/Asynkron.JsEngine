# ADR 0119: Keep computed member nullish read order spec ordered

## Status

Accepted

## Context

Issue #1752 / PR #1791 fixed the Test262
`language/expressions/member-expression/computed-reference-null-or-undefined.js`
crash for computed member reads on `null` and `undefined`.

The expression bytecode compiler and runner already had the pieces needed for
ordinary computed reads, optional computed reads, nullish-base validation, and
property-key resolution. The bug was their ordering for plain `base[key]`:
the runtime could reach property lookup with a nullish target and crash the host
instead of producing a catchable JavaScript `TypeError`.

Computed member reads have a subtle observable boundary. JavaScript evaluates
the base expression, then evaluates the computed property expression, then
requires the base to be object-coercible, then converts the property key and
performs the read. For a nullish base, key-expression side effects must still
run, but `ToPropertyKey` must not run after the nullish-base `TypeError`.
Optional computed reads keep the different contract: a nullish base
short-circuits before evaluating the key.

## Decision

Keep computed member read lowering and runtime execution as explicit ordered
steps:

1. evaluate the base expression;
2. evaluate the computed key expression for non-optional reads;
3. require the base to be object-coercible for non-optional reads;
4. resolve the property key only after the nullish-base check succeeds;
5. perform the property read.

For optional computed reads, keep the existing short-circuit marker and do not
emit the non-optional `RequireObjectCoercible` / `ResolvePropertyKey` sequence
before the optional `GetComputedProperty` operation.

Do not repair this class by routing computed member reads through the legacy AST
evaluator. The expression bytecode path owns the ordering and must carry enough
operation metadata to distinguish ordinary nullish TypeError behavior from
optional chaining short-circuit behavior.

## Consequences

- Future expression bytecode member-access changes must test both ordinary and
  optional computed nullish-base reads.
- Regression tests should prove all three observable points: key expression side
  effects run for ordinary `base[key]`, property-key conversion does not run
  after a nullish-base TypeError, and `base?.[key]` does not evaluate the key
  when the base is nullish.
- Keep the ordinary-member path aligned with the relevant Test262 fixture before
  widening to broader expression suites.
- This ADR is caused by issue #1752 / PR #1791 and complements ADR 0018's
  property-key ordering boundary for `super` references.
