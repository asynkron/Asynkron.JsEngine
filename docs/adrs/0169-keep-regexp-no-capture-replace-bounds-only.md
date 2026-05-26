# ADR 0169: Keep RegExp no-capture replace bounds-only

## Status

Accepted

## Context

Issue #2078 / PR #2092 reduced the `regex` benchmark allocation gap without
changing the existing RegExp cache or property-escape bridge behavior.

The baseline evidence showed the `regex` allocation profile dominated by
`String` and `Match` samples, and the comparison runner reported
`asynkron_kb=3133098.8` versus `jint_kb=779570.9` for a 4.02x allocation gap.
The owner path was the ordinary no-capture, plain-string `@@replace` fast path:
`RegExpPrototype.TryReplaceDefaultNoCapture` called
`JsRegExp.TryExecMatchOnly`, which materialized each matched value and updated
legacy RegExp statics for every match even though the replacement loop only
needed match index and length.

The merged delivery added `JsRegExp.TryExecMatchBoundsOnly` and changed the
ordinary no-capture replace path to consume bounds only. It records the final
successful match bounds and updates legacy RegExp statics once after replacement
assembly. The path remains guarded by ordinary default RegExp eligibility,
plain replacement text, no captures, no `/d` indices, no `$` substitutions, and
no observable custom `exec`.

## Decision

Keep no-capture plain-string RegExp replace allocation work at the bounds-only
runtime boundary:

1. If a replace path only needs match index and length, use a bounds-only helper
   instead of constructing match result strings or match arrays.
2. Apply this only after the existing ordinary default RegExp guards prove that
   `RegExpExec` is not user-observable: no own `exec`, default
   `RegExp.prototype.exec`, default flag accessors, no custom receiver shape,
   no captures, no `/d` indices, no functional replacement, and no `$`
   substitution processing.
3. Preserve `lastIndex` behavior, including global/sticky reset and
   zero-length advancement, exactly as the ordinary no-capture path requires.
4. Updating legacy RegExp statics once from the final successful match is safe
   only for this non-observable plain-string path, because no user code runs
   between accepted matches. Paths with callbacks, substitutions, captures,
   custom `exec`, or mutated accessors must fall back to the ordinary
   observable execution loop.
5. Keep the existing cache/property-escape boundaries unchanged. A bounds-only
   replace allocation win is not evidence to widen shared .NET `Regex` caching
   or property-escape shims.

## Consequences

- The retained slice reduced `regex` managed allocation from
  `asynkron_kb=3133098.8` to `asynkron_kb=2689348.5`, narrowing the allocation
  gap from 4.02x to 3.45x in the delivery benchmark evidence.
- Future RegExp replace performance work can avoid per-match value
  materialization when only bounds are needed, but must prove the path has no
  JavaScript-visible per-match execution point.
- Focused tests for this class of change should pin final legacy RegExp
  statics, `lastIndex`, global/sticky and zero-length behavior, `$`
  substitution fallback, overridden `exec` fallback, and capture/indices
  fallback.
- This complements ADR 0090: replace shortcuts are allowed, but only while the
  observable `RegExpExec` envelope is preserved.

## Related

- `docs/adrs/0090-keep-regexp-replace-shortcuts-observable-exec-safe.md`
- `docs/adrs/0112-keep-regexp-instance-cache-bounded-and-keyed-by-runtime-shape.md`
- `.claude/rules/ecmascript-regexp-runtime-bridges.md`
- `.claude/rules/performance-profiling-guardrails.md`
