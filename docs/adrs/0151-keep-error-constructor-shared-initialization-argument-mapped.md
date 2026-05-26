# ADR 0151: Keep Error constructor shared initialization argument-mapped

## Status

Accepted

## Context

Issue `autrun-dis9soildbzc-4042b4c64c` / PR #1991 continued the recurring
code-reduction work in `StdLib/Error`.

Before the delivery, `ErrorConstructorBase.InitializeError` and
`AggregateErrorConstructor.InitializeError` both performed the same common
Error-object initialization:

- assign the instance realm state when absent;
- create the internal `_errorData` marker;
- install the optional non-enumerable `message` data property; and
- install the optional non-enumerable `cause` data property from
  `options.cause`.

The duplication was structural, but the argument mapping was not identical.
Native Error constructors use `(message, options)`, while `AggregateError`
uses `(errors, message, options)` and then performs its own iterable-to-list
work before defining `errors`.

The accepted delivery extracted the common initialization into
`ErrorConstructorBase.InitializeErrorShared(instance, messageArg, optionsArg)`.
`ErrorConstructorBase.InitializeError` now maps the ordinary Error-family
arguments before calling the helper, while `AggregateErrorConstructor` maps the
shifted `message` and `options` arguments explicitly and keeps only the
AggregateError-specific `errors` handling locally.

Focused proof:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~AggregateError|FullyQualifiedName~ErrorConstructor"
```

## Decision

Keep shared Error-object initialization in `ErrorConstructorBase`, but make
constructor-specific argument mapping explicit before entering the shared
helper.

Future Error-constructor refactors should:

1. put shared `_errorData`, `message`, and `cause` initialization in
   `InitializeErrorShared`;
2. keep ordinary Error-family argument mapping in the base virtual
   `InitializeError`;
3. keep `AggregateError`'s shifted `(errors, message, options)` mapping in
   `AggregateErrorConstructor` before calling the shared helper; and
4. leave `AggregateError` iterable collection and `errors` property definition
   outside the shared Error initialization helper.

Do not deduplicate Error constructors by passing raw `args` into a generic
helper that has to infer constructor family from arity or type. The call site
that owns the constructor grammar should choose `messageArg` and `optionsArg`.

## Consequences

- Common Error metadata and ES2022 `cause` behavior stay in one implementation.
- `AggregateError` keeps its spec-visible argument order clear and avoids
  treating `errors` as `message`.
- Future changes to `message` or `cause` behavior should prove both ordinary
  Error-family constructors and `AggregateError`.
- Duplicate-code tools can identify the shared block, but review must preserve
  the constructor-specific argument mapping and the AggregateError-only
  iterable path.

## Related

- `.claude/rules/ecmascript-error-constructors.md`
- `src/Asynkron.JsEngine/StdLib/Error/ErrorConstructorBase.cs`
- `src/Asynkron.JsEngine/StdLib/Error/AggregateErrorConstructor.cs`
