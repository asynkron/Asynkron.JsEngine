# ADR 0202: Keep SetRecord key traversal early-exit helper owned

## Status

Accepted

## Context

Issue `autrun-disva4a5rnxs-787e8d6f1e` / PR #2210 was a recurring
code-reduction child that targeted duplicated Set-like key traversal in
`SetPrototype`.

`IterateSetRecordKeys` and `IterateSetRecordKeysWithEarlyExit` had the same
iterator acquisition, `next` validation, iterator-result validation, `done`
handling, value extraction, and numeric `-0` normalization. The only semantic
difference was close ownership: full traversal must consume the iterator
without calling `return()`, while early-exit callers must close the iterator
when the callback stops traversal.

The accepted delivery made the non-early-exit helper delegate to the
early-exit-capable helper with a callback that always returns `true`. That
removed the duplicate traversal body without changing close behavior.

Focused proof used:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~SetMethodsTests|FullyQualifiedName~SetTests"
```

The Set proof pack passed 62 tests.

## Decision

Keep SetRecord key traversal owned by the early-exit-capable helper.

For future Set.prototype set-algebra work:

1. keep iterator acquisition, `next` lookup, iterator result validation,
   `done` handling, value extraction, and numeric `-0` normalization in
   `IterateSetRecordKeysWithEarlyExit`;
2. make full-traversal callers delegate with an always-continue callback
   instead of copying the iterator loop;
3. call iterator `return()` only when traversal stops early because the
   callback returned `false`;
4. do not introduce a flag parameter that hides whether a caller is full
   traversal or early-exit traversal; keep call-site intent visible; and
5. prove changes with the Set algebra tests plus any new tests for observable
   iterator closing behavior when that behavior is touched.

## Consequences

- Set.prototype set-algebra helpers have one owner for Set-like key iteration
  protocol details.
- Future fixes for iterator validation, completion, or signed-zero
  normalization should land in the shared helper rather than in parallel loops.
- The full traversal path remains observably distinct from early exit because
  it does not call iterator `return()` after ordinary exhaustion.
- The general recurring-code-reduction policy remains in
  `docs/rules/recurring-maintenance-child-runs.md`; this ADR records the
  Set-specific semantic owner.

## Related

- Issue `autrun-disva4a5rnxs-787e8d6f1e`
- PR #2210
- `docs/rules/recurring-maintenance-child-runs.md`
- `src/Asynkron.JsEngine/StdLib/MapSet/SetPrototype.cs`
