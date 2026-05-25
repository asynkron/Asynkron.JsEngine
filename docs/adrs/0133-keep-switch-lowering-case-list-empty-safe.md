# ADR 0133: Keep switch lowering case-list empty-safe

## Status

Accepted

## Context

Issue #1841 / PR #1878 fixed a crash in `SwitchEmitter.TryEmitSwitch` for an
empty switch case-list:

```js
eval('1; switch ("a") { }')
```

The switch emitter lowers a `switch` into synthetic discriminant, match-index,
and done variables plus guarded case-body blocks. That lowering also needs a
strictness flag for synthetic blocks. Before PR #1878, some synthetic
matching/default blocks read `statement.Cases[0].Body.IsStrict` directly. That
was safe only for switches with at least one clause, so the grammar-valid empty
case-list shape could crash before switch completion reached the expected
`undefined` result.

The delivery kept the fix narrow: compute one guarded `switchIsStrict` value
from the first case only when a case exists, then reuse it for matching blocks
and the lowered outer/inner switch blocks. It also added focused internal
coverage in `SwitchCompletionSimpleTests`.

## Decision

Switch lowering must treat an empty case-list as a first-class grammar shape.
Emitter metadata that is derived from case bodies, such as synthetic block
strictness, must be guarded by `statement.Cases.Length > 0` before indexing
`statement.Cases[0]`.

When the metadata applies to the whole lowered switch rather than a specific
case body, compute it once near the top of `TryEmitSwitch` and reuse that local
value consistently across the synthetic matching/default guard blocks and the
lowered switch block wrappers.

Do not fix this class by adding runner-time switch special cases or by treating
empty switches as unsupported lowering. The switch emitter already owns the
completion initialization and synthetic bookkeeping, so the safe boundary is
lowering-time normalization.

## Consequences

- Future switch-emitter changes must include the empty case-list shape when
  deriving metadata from case clauses.
- Regression proof for switch completion bugs should include at least one
  no-clause switch, plus the reported fallthrough or abrupt-empty shapes when
  those are the issue trigger.
- Synthetic switch bookkeeping assignments must remain suppressed as completion
  values; the empty switch result remains `undefined`.
- This decision complements `.claude/rules/switch-lowering-completion.md`.

## Traceability

- Caused by issue #1841 / PR #1878.
- Delivery commit: `3c19448d` (`Fix switch emitter empty-case strictness crash
  (#1878)`).
