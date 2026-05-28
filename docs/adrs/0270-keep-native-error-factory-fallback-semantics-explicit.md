# ADR 0270: Keep Native Error factory fallback semantics explicit

## Status

Accepted

## Context

Issue `autrun-diulo4a3l52o-d05a9ba04d` / PR #2603 continued recurring
code-reduction work in `StandardLibrary.cs`.

Before the delivery, `CreateTypeError`, `CreateRangeError`,
`CreateReferenceError`, `CreateSyntaxError`, and `CreateURIError` each repeated
the same native error construction shape:

- resolve the active realm from the explicit realm, evaluation context, or
  `RealmState.Current`;
- look up the realm-specific native error constructor;
- invoke it with the message argument; and
- fall back to a plain error object when the constructor is absent or returns
  `undefined`.

The repeated shape was safe to share, but the fallback policy was not identical:

- `TypeError`, `ReferenceError`, and `SyntaxError` must use their specific
  prototypes in fallback objects when available;
- `RangeError` and `URIError` intentionally use the generic error prototype
  fallback;
- `ReferenceError` also falls back when the constructor returns `null`; and
- `ThrowReferenceError` owns the constructor-property repair flow after creating
  the error value.

The accepted delivery introduced `CreateNativeError(...)` for the shared realm
resolution, constructor invocation, and fallback plumbing while keeping those
per-error differences explicit at the call sites.

Focused proof:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --filter "FunctionRealmTests|JsOpsTests" -c Release --nologo
```

Canonical quality proof from the delivery lifecycle:

```bash
rtk make quality
```

## Decision

Keep `StandardLibrary.cs` native error factory deduplication limited to the
shared constructor lookup, invocation, and fallback mechanics.

Future native error factory refactors should:

1. keep each error-family call site responsible for naming its constructor and
   fallback prototype policy;
2. preserve `ReferenceError` fallback on both `undefined` and `null` constructor
   results;
3. preserve `TypeError`, `ReferenceError`, and `SyntaxError` specific fallback
   prototypes;
4. preserve `RangeError` and `URIError` generic error fallback behavior; and
5. keep `ThrowReferenceError` constructor-property repair outside the shared
   factory helper.

Do not collapse native error helpers into a generic error-name-only helper that
infers prototype policy, null fallback behavior, or throw-time repair behavior
from the error name.

## Consequences

- Native error construction stays smaller without hiding observable realm and
  prototype behavior.
- Future code-reduction slices have a concrete boundary for what belongs in the
  shared helper and what must remain family-specific.
- Focused proof should cover realm-sensitive `TypeError` / `ReferenceError`
  behavior and catchable runtime `TypeError` behavior before widening.

Related rule:

- `.claude/rules/ecmascript-error-constructors.md`
