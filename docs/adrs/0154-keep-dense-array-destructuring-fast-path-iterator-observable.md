# ADR 0154: Keep dense array destructuring fast path iterator-observable

## Status

Accepted

## Context

Issue `autrun-disb2md7n23s-0c3cde8865` / PR #1999 selected the
`destructuring` benchmark because dense array binding declarations were one of
the largest current Asynkron-vs-Jint losses.

The retained optimization added a narrow direct binding path for fresh dense
`JsArray` values whose array pattern is identifier-only, has no defaults/rest,
uses present own elements for every consumed index, and still resolves
`@@iterator` to the native array `values` function. It also added a fast path
for standard array iterator result objects that do not escape.

The review/build-back repair was semantic: the direct dense path initially
treated a default array iterator as unconditionally safe. That missed the
observable `IteratorClose` surface when
`%ArrayIteratorPrototype%.return` is installed. On abrupt assignment
completion, such as assigning into a `const` target, array destructuring must
fall back to the normal iterator path so `return()` is called and validated.

The final implementation therefore keeps the dense direct path only when the
array has the default `values` iterator, the array iterator prototype still has
the native `next`, and the array iterator prototype has no observable
`return`.

## Decision

Dense array destructuring shortcuts must prove the iterator protocol surface is
unobservable before bypassing the generic iterator driver.

For array binding or assignment destructuring fast paths:

1. require the receiver to be an ordinary dense `JsArray` with present own
   elements for the consumed range;
2. require the array's `@@iterator` lookup to resolve to the native `values`
   function;
3. require `%ArrayIteratorPrototype%.next` to remain the native `next`
   function;
4. fall back when `%ArrayIteratorPrototype%.return` is present and non-nullish;
5. fall back for holes, indexed descriptors, defaults, rest, nested targets,
   custom iterators, and generator/suspending contexts; and
6. keep abrupt completion and `IteratorClose` behavior owned by the generic
   path rather than duplicating close semantics in the dense shortcut.

## Consequences

- Future destructuring performance work must treat native iterator identity as
  necessary but not sufficient. Observable `return()` hooks are part of the
  guard.
- Direct element binding can stay allocation- and dispatch-light for the
  benchmark shape without weakening custom iterator or abrupt close semantics.
- Regression coverage for this class should include both positive fast-path
  behavior and negative guard cases: custom `Array.prototype[Symbol.iterator]`,
  custom `%ArrayIteratorPrototype%.next`, and
  `%ArrayIteratorPrototype%.return` during abrupt assignment completion.
- Performance reports should pair the selected-profile timing/call-tree win
  with focused semantic tests that prove the fallback guards.

## Related

- `docs/performance/destructuring-dense-array-fast-path.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `docs/adrs/0089-keep-suspended-array-pattern-iterators-resumable-and-closable.md`
- `docs/adrs/0129-keep-destructuring-step-throw-iterator-close-spec-ordered.md`
