# ADR 0052: Keep dynamic with-scope cleanup boundaries identity-based

## Status

Accepted

## Context

Issue #830 / PR #1131 fixed the Test262 `Statements_with` cluster. The first
repair handled object-environment binding semantics, but the delivery then
exposed a separate IR cleanup bug: abrupt control flow inside a `with` frame
could over-pop the enclosing dynamic object environment.

The previous cleanup boundary model was integer-only. Lexical scopes had stable
scope ids, but `with` frames did not, so the dynamic frame used a negative
sentinel. That made nested `break` cleanup treat the enclosing `with` boundary
as unbounded cleanup. A `break` from a loop or switch nested inside `with`
could therefore remove the `with` frame that should remain visible after the
abrupt statement completed.

Review then found the same rule had to be applied consistently to loop, switch,
and labeled-break target scopes. Fixing only the original loop shape left
switch break cleanup with the older enclosing-scope id behavior.

## Decision

IR abrupt control-flow cleanup boundaries must represent both lexical scopes and
dynamic `with` frames. A lexical cleanup boundary may use the lexical scope id,
but a dynamic `with` boundary must be identified by the actual slot/frame
identity that owns the object environment.

Emitters that register break or continue targets while a `with` frame is active
must capture the current `ScopeExitBoundary`, not just the current lexical
scope id. Cleanup construction must stop at a matching dynamic `with` frame
instead of interpreting the lack of lexical scope id as permission to unwind all
dynamic scopes.

This decision extends ADR 0004. Cleanup chains remain explicit IR instructions;
the boundary metadata used to build those chains must be rich enough to express
the scopes crossed by JavaScript execution, including dynamic object
environments introduced by `with`.

## Consequences

- Do not reintroduce integer-only break/continue cleanup boundaries for IR
  emitters that can run inside `with`.
- When adding or changing loop, switch, labeled statement, or other abrupt
  control-flow emitters, verify both lexical scope cleanup and dynamic
  `with`-frame preservation.
- Regression tests for this class should include `break` out of loops and
  switches nested inside an enclosing `with`, proving property lookup after the
  break still resolves through the object environment.
- The issue-specific proof remains the focused Test262
  `Name=Statements_with` group plus internal regressions around the affected
  control-flow shape; a full Test262 run is not required for this issue class.

## Traceability

- Caused by issue #830 / PR #1131.
- Complements ADR 0004's explicit IR abrupt-control-flow cleanup contract and
  `.claude/rules/ir-control-flow-cleanup.md`.
