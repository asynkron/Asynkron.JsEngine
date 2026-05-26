# ECMAScript Error Constructors

When changing `StdLib/Error` constructors, keep shared Error-object
initialization separate from constructor-specific argument mapping.

## Rules

1. Keep shared `_errorData`, optional `message`, and optional `cause`
   initialization in `ErrorConstructorBase.InitializeErrorShared`.
2. Map constructor arguments before calling the shared helper. Ordinary
   Error-family constructors map `(message, options)` in
   `ErrorConstructorBase.InitializeError`; `AggregateError` maps
   `(errors, message, options)` in `AggregateErrorConstructor`.
3. Do not make the shared helper inspect raw `args`, constructor names, or arity
   to decide which values are `message` and `options`. The constructor that
   owns the ECMAScript grammar owns that mapping.
4. Keep `AggregateError` iterable collection and `errors` property definition
   outside the shared Error initialization helper.
5. When changing `message`, `cause`, `_errorData`, or constructor argument
   mapping, prove both ordinary Error-family constructors and `AggregateError`
   with a focused filter before widening.

## Why

Issue `autrun-dis9soildbzc-4042b4c64c` / PR #1991 removed duplicated
initialization logic from `ErrorConstructorBase` and `AggregateErrorConstructor`.
The useful lesson was not "merge all Error constructor logic"; it was to share
only the invariant Error-object setup while leaving `AggregateError`'s shifted
argument mapping and `errors` creation local. Without this boundary, a future
cleanup can accidentally treat the `errors` iterable as the message argument or
hide spec-visible constructor grammar behind a generic raw-args helper.

Focused proof from the incident:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~AggregateError|FullyQualifiedName~ErrorConstructor"
```

Related ADR:

- `docs/adrs/0151-keep-error-constructor-shared-initialization-argument-mapped.md`
