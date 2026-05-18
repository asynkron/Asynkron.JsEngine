# ADR 0004: Keep IR abrupt control-flow scope cleanup explicit

## Status

Accepted

## Context

Issue #756 fixed the Test262 `BlockScope_leave` failures for
`language/block-scope/leave/x-after-break-to-label.js`. The root bug was not the
label lookup itself: emitted IR for labeled non-loop blocks could send `break`
directly to the labeled exit and bypass the lexical block's
`PopEnvironmentInstruction`. That left `let` bindings visible after the labeled
break and skipped the normal pop/dispose/pooling path.

The first delivery repair routed abrupt control flow through emitted lexical
scope cleanup chains. A later quality-gate re-entry exposed a second edge: a
same-loop `continue` inside `for-of` can also be wrapped by one or more
`PopEnvironmentInstruction`s. The for-of try/finally guard had compared the
raw target index to the loop continue target, so it misclassified cleanup-wrapped
same-loop continues as loop exits and ran iterator close/finally.

## Decision

Abrupt IR control flow that crosses lexical scopes must target emitted cleanup
chains, not jump directly past block cleanup.

Runtime checks that need to classify an abrupt target, such as deciding whether
a `for-of` `continue` stays in the same loop, must resolve through any leading
`PopEnvironmentInstruction` chain before comparing the target to loop metadata.
The cleanup instructions remain real instructions so environment disposal,
pooling metadata, and normal scope unwinding stay centralized in the existing
scope handlers.

Do not replace this with runtime-only environment rewinds unless a future change
proves equivalent pop/dispose/pooling semantics for every crossed scope.

## Consequences

- Emitters for `break`, `continue`, labeled blocks, loops, and nested lexical
  scopes must preserve cleanup-chain construction when changing target logic.
- Same-loop `continue` detection must treat `PopEnvironmentInstruction` chains
  as transparent wrappers around the real target.
- Regression coverage should include both the external conformance symptom
  (`BlockScope_leave`) and local checks for lexical binding leakage and
  same-loop continue behavior through nested cleanup.
