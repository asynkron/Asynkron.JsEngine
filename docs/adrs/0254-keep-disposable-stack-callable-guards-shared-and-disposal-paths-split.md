# ADR 0254: Keep DisposableStack callable guards shared and disposal paths split

## Status

Accepted

## Context

Issue `autrun-diu4yqab0uq8-1e1088957e` / PR #2494 was a recurring
code-reduction child that targeted duplicated callable-disposer validation in
`DisposableStackPrototype` and `AsyncDisposableStackPrototype`.

The duplicated code in `adopt` and `defer` had the same shape in both
prototypes:

1. read the disposer argument;
2. require it to unwrap as `IJsCallable`;
3. throw a TypeError with the prototype-specific message if it is not callable;
   and
4. add the disposal record with the original call-site arguments.

The safe reduction was to move only the callable guard into
`DisposableStackHelper.RequireCallable(...)`. The call sites still pass the
exact TypeError text, so `DisposableStack.prototype.*` and
`AsyncDisposableStack.prototype.*` messages remain observable and distinct.

The delivery deliberately did not merge `dispose` and `disposeAsync`.
Synchronous disposal returns or throws directly, while async disposal creates a
promise and resolves or rejects through that promise. Treating those as one
"duplicated" path would change the observable JavaScript contract.

## Decision

Keep callable-disposer validation shared through `DisposableStackHelper`, but
keep disposal completion behavior owned by each prototype method.

For future `DisposableStack` / `AsyncDisposableStack` maintenance:

1. use `DisposableStackHelper.RequireCallable(...)` for identical `adopt` and
   `defer` callable-disposer validation;
2. keep prototype-specific TypeError messages at the call sites;
3. do not merge direct synchronous `dispose` completion with promise-producing
   `disposeAsync` completion;
4. do not hide sync/async behavior behind a boolean mode or a generic disposal
   runner unless focused behavior tests prove the observable contract is
   unchanged; and
5. pair code-size evidence with a focused search or static-analysis check over
   the affected stdlib folders.

## Consequences

- Future fixes to callable-disposer unwrapping can land in one helper.
- Observable error messages remain caller-owned and easy to audit.
- Sync throw/return behavior and async promise resolve/reject behavior remain
  separate.
- Recurring code-reduction slices have a narrow path for reducing guard
  boilerplate without reshaping disposal semantics.

## Related

- Issue `autrun-diu4yqab0uq8-1e1088957e`
- PR #2494
- `.claude/rules/recurring-maintenance-child-runs.md`
- `src/Asynkron.JsEngine/StdLib/DisposableStack/DisposableStackHelper.cs`
- `src/Asynkron.JsEngine/StdLib/DisposableStack/DisposableStackPrototype.cs`
- `src/Asynkron.JsEngine/StdLib/AsyncDisposableStack/AsyncDisposableStackPrototype.cs`
