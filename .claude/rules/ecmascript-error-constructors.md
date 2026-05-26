# ECMAScript Error Constructors

When changing `StdLib/Error` constructors, keep shared Error-object
initialization separate from constructor-specific argument mapping.

## Rules

1. Keep shared `_errorData`, optional `message`, and optional `cause`
   initialization in `ErrorConstructorBase.InitializeErrorShared`.
2. Map constructor arguments before calling the shared helper. Ordinary
   Error-family constructors map `(message, options)` in
   `ErrorConstructorBase.InitializeError`; `AggregateError` maps
   `(errors, message, options)` in `AggregateErrorConstructor`;
   `SuppressedError` maps `(error, suppressed, message, options)` in
   `SuppressedErrorConstructor`.
3. Do not make the shared helper inspect raw `args`, constructor names, or arity
   to decide which values are `message` and `options`. The constructor that
   owns the ECMAScript grammar owns that mapping.
4. Keep `AggregateError` iterable collection and `errors` property definition
   outside the shared Error initialization helper. Keep `SuppressedError`
   `error` and `suppressed` property definitions outside the helper as well.
5. When changing `message`, `cause`, `_errorData`, or constructor argument
   mapping, prove ordinary Error-family constructors plus shifted-argument
   constructors such as `AggregateError` and `SuppressedError` with focused
   filters before widening.

## Why

Issue `autrun-dis9soildbzc-4042b4c64c` / PR #1991 removed duplicated
initialization logic from `ErrorConstructorBase` and `AggregateErrorConstructor`.
The useful lesson was not "merge all Error constructor logic"; it was to share
only the invariant Error-object setup while leaving `AggregateError`'s shifted
argument mapping and `errors` creation local. Without this boundary, a future
cleanup can accidentally treat the `errors` iterable as the message argument or
hide spec-visible constructor grammar behind a generic raw-args helper.

Issue `autrun-disf6pig3gd4-944c449a56` / PR #2039 found the same duplicated
common initialization in `SuppressedErrorConstructor`. The fix mapped
`message` and `options` locally, called `InitializeErrorShared`, and then kept
the `error` and `suppressed` properties local to `SuppressedError`. Without this
follow-up rule, future cleanup can either reintroduce duplicated `_errorData`,
`message`, and `cause` setup or overgeneralize the helper until it hides the
constructor-specific `(error, suppressed, message, options)` grammar.

Focused proof from the incident:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~AggregateError|FullyQualifiedName~ErrorConstructor"
```

Focused proof from the SuppressedError incident:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --filter "FullyQualifiedName~ErrorTypesTests" --configuration Release
```

Related ADR:

- `docs/adrs/0151-keep-error-constructor-shared-initialization-argument-mapped.md`
