# ADR 0153: Keep destructuring ToObject coercion JsValue-native

## Status

Accepted

## Context

Issue `autrun-disb2mdhzcvs-0a8e873051` / PR #1996 continued the core
`object?` carrier cleanup in the typed AST evaluator.

Before the delivery, `ToObjectForDestructuringJsValue` accepted a `JsValue` but
then manually unwrapped primitive cases before calling
`StandardLibrary.TryGetObject(object?, realm, out ...)`:

1. booleans became CLR `bool`;
2. numbers became CLR `double`;
3. every other value flowed through `ObjectValue`.

That recreated a local object-carrier bridge in a private evaluator helper that
already had the JavaScript value. It also split destructuring primitive boxing
from the shared `StandardLibrary.TryGetObject(JsValue, ...)` path that owns
`JsValue`-native ToObject coercion.

The accepted delivery removed the manual primitive switch and passed the
original `JsValue` directly to `StandardLibrary.TryGetObject(jsValue, realm,
out var coerced)`. The slice was deliberately narrow: no public API,
debug/diagnostic, host interop, or `ConditionalWeakTable object?` surfaces were
changed.

Focused proof used:

- baseline/final search for the retired primitive-switch comment and the new
  `StandardLibrary.TryGetObject(jsValue, ...)` call in
  `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.cs`;
- `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~Destructuring"`;
- result: 152 destructuring tests passed, with only existing nullable warnings
  in test projects.

## Decision

Keep destructuring ToObject coercion on the shared `JsValue` standard-library
path.

For private destructuring helpers:

1. preserve nullish rejection before primitive boxing;
2. keep the direct `IJsObjectLike` fast path for values that are already object
   wrappers;
3. call `StandardLibrary.TryGetObject(JsValue, realm, out ...)` for primitive
   boxing and object-like coercion;
4. do not manually unwrap primitive `JsValue` cases into CLR payloads before
   coercion; and
5. prove future cleanup with a targeted legacy-switch search plus the focused
   destructuring test pack before widening into unrelated evaluator helpers.

## Consequences

- Destructuring remains aligned with the core `JsValue` value-carrier contract:
  evaluator internals should not turn JavaScript values into `object?` unless
  an intentional boundary requires it.
- Future primitive-boxing behavior should be fixed in the shared
  `StandardLibrary.TryGetObject(JsValue, ...)` helper, not in a
  destructuring-local switch.
- The legacy `StandardLibrary.TryGetObject(object?, ...)` overload can remain
  for callers that still own an intentional object boundary, but destructuring
  is no longer one of those callers.
- This ADR complements ADR 0123's typed object-extraction guardrail and ADR
  0148's context-aware property-read receiver rule by covering the
  destructuring ToObject coercion boundary.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0123-keep-number-receiver-object-extraction-typed-and-accessor-compatible.md`
- `docs/adrs/0148-keep-context-property-reads-jsvalue-receiver-native.md`
