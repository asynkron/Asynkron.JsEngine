# ADR 0148: Keep context property reads JsValue receiver-native

## Status

Accepted

## Context

Issue `autrun-dis78svbpuvk-6736b1535b` / PR #1966 continued the bounded
core-runtime cleanup that removes legacy `object?` carriers from JavaScript
value flows.

Before the delivery, `JsOps` had context-aware property-read branches that
looked up primitive prototype properties by passing extracted CLR payloads such
as `target.AsBoolean()`, `target.NumberValue`, or a general `object? target`
into `JsObject.TryGetProperty(...)`. The result then had to be normalized with
defensive bridges such as `value is JsValue jv ? jv :
JsValue.FromObjectUnsafe(value)`.

That carrier shape was not a public, host-interop, debugger, or diagnostic
boundary. It was a private JavaScript property-access flow that already had a
`JsValue` receiver at the caller. Passing the extracted CLR payload through the
context-aware object lookup path kept avoidable boxing and rewrapping in the
runtime helper, and it made future accessor/prototype work easier to route back
through the wrong overload.

The accepted delivery added a `JsObject.TryGetProperty(string name, JsValue
receiver, EvaluationContext? context, out JsValue value)` entry point and moved
the `JsOps` context-aware read branches to pass `JsValue` receivers directly.
For legacy `object?` callers that still enter the selected helper, the boundary
is now explicit at the callsite with `JsValue.FromObjectUnsafe(target)` rather
than hidden behind the property lookup overload.

Focused proof used a before/after search for context-aware `TryGetProperty`
calls in `JsOps`/`JsObject`, plus:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~JsOpsTests|FullyQualifiedName~ObjectDescriptorTests|FullyQualifiedName~PrivateFieldsTests"
```

The focused pack passed 91/91.

## Decision

Keep context-aware private property-read helpers receiver-native in `JsValue`
when the caller is already operating on JavaScript values.

For `JsOps`, `JsObject`, and adjacent runtime property-access helpers:

1. Prefer overloads that accept `JsValue receiver` and return `JsValue` for
   context-aware property reads.
2. Do not pass extracted CLR primitive payloads such as `bool`, `double`,
   `string`, `JsBigInt`, or general `object?` through a JavaScript property
   lookup path when a `JsValue` receiver is already available.
3. If an unmigrated legacy object-carrier branch still calls the typed helper,
   make the conversion explicit at that branch with `JsValue.FromObjectUnsafe`
   and keep that branch as the remaining migration target.
4. Preserve accessor and throw propagation semantics by keeping the active
   `EvaluationContext?` on the property-read path.
5. Prove each slice with a focused before/after search for the legacy
   context-aware `TryGetProperty(..., object?, context, out ...)` shape and the
   owning semantic tests.

## Consequences

- Future object-to-`JsValue` migration slices should treat property-read
  receivers as part of the JavaScript value carrier, not as an implementation
  detail that can be unboxed before prototype/accessor lookup.
- Existing public or compatibility object-carrier overloads may remain only
  where they own a real boundary. Private runtime flows should route through the
  `JsValue` overload and delete obsolete bridges once no selected callers need
  them.
- This complements ADR 0081's active-context propagation rule and ADR 0123's
  typed object-extraction compatibility rule. ADR 0148 owns the context-aware
  property-read receiver carrier boundary.
- This ADR is caused by issue `autrun-dis78svbpuvk-6736b1535b` / PR #1966.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0081-keep-prototype-host-method-context-propagation.md`
- `docs/adrs/0123-keep-number-receiver-object-extraction-typed-and-accessor-compatible.md`
