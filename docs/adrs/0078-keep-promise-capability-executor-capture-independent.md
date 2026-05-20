# ADR 0078: Keep Promise capability executor capture independent

## Status

Accepted

## Context

Issue #1056 / PR #1288 fixed the focused Test262
`Promise_prototype_then` regression for
`built-ins/Promise/prototype/then/capability-executor-called-twice.js`.

`Promise.prototype.then` constructs a promise capability through a local
`NewPromiseCapability` helper. The old helper used one combined duplicate-call
guard for the resolve/reject pair and one combined callable validation error.
That shape looked equivalent for ordinary constructors, but it did not match
the constructor helper's spec-shaped behavior when a custom species constructor
calls the executor more than once with different captured values.

The observable split matters because the executor captures `resolve` and
`reject` independently. A second executor call must fail as soon as either
captured slot was already set, and callable validation must report the missing
or non-callable resolve and reject path separately after construction returns.
Collapsing the two slots into a single pair-level guard hides which abstract
operation boundary was crossed.

## Decision

Keep Promise capability construction helpers aligned with the ECMAScript
`NewPromiseCapability` shape:

- initialize resolve and reject capture slots independently;
- reject a duplicate executor call after either capture slot has been set;
- validate captured resolve and reject callability separately after
  construction;
- keep `Promise.prototype.then` species-constructor capability behavior aligned
  with the main `Promise` constructor helper instead of maintaining a looser
  prototype-local variant.

## Consequences

- Future Promise species-constructor work should compare prototype-local
  capability helpers with the constructor helper before changing duplicate-call
  or callable-validation behavior.
- Focused proof should include the exact Test262 fixture
  `built-ins/Promise/prototype/then/capability-executor-called-twice.js` in
  both strict and non-strict modes, plus the owning
  `Name=Promise_prototype_then` Test262 method group.
- Review should treat combined resolve/reject guards in Promise capability code
  as suspicious unless a spec proof shows the slots cannot be observed
  independently.
- This ADR is caused by issue #1056 / PR #1288.
