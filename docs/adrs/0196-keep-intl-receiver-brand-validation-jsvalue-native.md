# ADR 0196: Keep Intl receiver brand validation JsValue-native

## Status

Accepted

## Context

Issue `autrun-diss73i9hwy8-488bb99c17` / PR #2192 continued the recurring
object-to-`JsValue` cleanup by targeting Intl receiver-brand validation.

`IntlBrandExtensions` already exposed a `JsValue` receiver overload for
`EnsureBrand(...)`, but it still kept an older `object?` extension overload that
accepted only `JsObject` payloads directly. Intl prototype entrypoints already
carry method receivers as `JsValue`, so the `object?` overload was not a public
facade, host interop, debugger, or diagnostic boundary. It was a private
standard-library helper path that could accidentally keep future Intl code on a
boxed object-carrier route through C# extension overload resolution.

The investigation plan allowed either marking the legacy overload with
`[Obsolete(..., true)]` to expose hidden callsites or deleting it if focused
evidence proved there were no active callers. The accepted delivery removed the
legacy overload outright because the selected cluster already had complete
`JsValue` coverage.

Focused proof used:

```bash
rtk rg -n "EnsureBrand\(this object\?|\.EnsureBrand\(" src/Asynkron.JsEngine/StdLib/Intl
rtk rg -n "EnsureBrand\(this object\?" src/Asynkron.JsEngine/StdLib/Intl/IntlBrandExtensions.cs
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release
```

The baseline showed the legacy overload plus Intl callsites. The final searches
showed only `.EnsureBrand(...)` callsites and no `EnsureBrand(this object? ...)`
definition, and the focused Release build passed with 0 errors and 0 warnings.

## Decision

Keep Intl receiver-brand validation `JsValue`-native. The standard-library
helper should accept the receiver as `JsValue`, extract the runtime object with
`TryGetObject(...)`, check the brand marker, and throw the existing TypeError
for incompatible receivers.

For future Intl brand-validation cleanup:

1. do not reintroduce `object?` receiver overloads for private Intl brand
   checks;
2. delete obsolete private object-carrier overloads when focused searches show
   no internal callers and a typed `JsValue` path owns the semantics;
3. use `[Obsolete(..., true)]` only as temporary compiler pressure when the
   overload must remain long enough to expose real callers or preserve an
   intentional boundary; and
4. prove the selected slice with a scoped signature/callsite search plus a
   focused compile or semantic proof for the affected Intl cluster.

## Consequences

- Intl brand checks no longer have a private boxed receiver convenience path.
- C# extension overload resolution cannot silently bind future core Intl
  receivers to the retired `object?` helper.
- Primitive receivers still fail through the shared `JsValue` brand check and
  preserve the existing incompatible-receiver TypeError behavior.
- A future public or host interop compatibility boundary must make conversion
  explicit at that boundary rather than restoring a private core overload.

## Related

- `.claude/rules/jsvalue-core-values.md`
