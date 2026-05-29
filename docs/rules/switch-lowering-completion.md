# Switch Lowering Completion

When changing `SwitchEmitter` or nearby switch completion lowering, keep
grammar-empty case-lists and completion-record behavior explicit.

## Rules

1. Treat `switch (expr) { }` as a valid lowered switch shape. Do not index
   `statement.Cases[0]` until `statement.Cases.Length > 0` has been proven.
2. If synthetic switch-block metadata is derived from case bodies, compute one
   guarded switch-level value and reuse it across matching/default guard blocks
   and lowered outer/inner block construction.
3. Keep empty case clauses and empty case-lists from overwriting the switch
   completion value. The switch emitter's explicit `undefined` initialization
   is the owner for empty-switch completion.
4. Keep synthetic switch bookkeeping assignments, such as match-index and done
   flags, suppressed as observable script completion values.
5. Prefer lowering-time normalization in `SwitchEmitter` over runner-time
   switch special cases for completion or empty-clause behavior.
6. Prove this area with focused internal switch completion tests first. Include
   a no-clause switch regression when touching case-list-wide metadata, then
   widen to exact issue-listed `Statements_switch` rows or the method group
   when needed.

## Why

Issue #1841 / PR #1878 fixed an empty-switch crash where
`SwitchEmitter.TryEmitSwitch` derived synthetic block strictness from
`statement.Cases[0].Body.IsStrict`. That assumption skipped the grammar-valid
empty case-list shape, so `eval('1; switch ("a") { }')` could crash instead of
completing as `undefined`. Future switch lowering work must guard case-derived
metadata and keep completion semantics owned by the emitter rather than adding
fallback behavior in the runner. Related ADR:
`docs/adrs/0133-keep-switch-lowering-case-list-empty-safe.md`.
