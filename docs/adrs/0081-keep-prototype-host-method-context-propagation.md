# ADR 0081: Keep prototype host method context propagation explicit

## Status

Accepted

## Context

Issue #1053 / PR #1286 fixed the focused Test262
`Object_prototype_propertyIsEnumerable` crash for
`built-ins/Object/prototype/propertyIsEnumerable/symbol_property_valueOf.js`.

`Object.prototype.propertyIsEnumerable` converted its property-key argument
through `JsOps.ToPropertyName(args[0])` without the active
`EvaluationContext`. That looked harmless for primitive string and symbol keys,
but object property keys are observable: `ToPropertyKey` can call
`valueOf`, `toString`, or `Symbol.toPrimitive` in the caller's JavaScript
context. The failing fixture used a wrapper whose `valueOf` returned a Symbol,
so the context-free shortcut could not reliably run and propagate that
conversion as an ordinary JavaScript completion.

The repair also exposed a source-generator boundary. Standalone host functions
already supported `EvaluationContext?` signatures through
`SetInvokeWithContext`, but prototype host methods did not. Adding context only
to `ObjectPrototype.PropertyIsEnumerable` would not be enough unless the
generated prototype registration routed normal JavaScript calls through the
context-aware hook.

## Decision

Prototype host methods may take an optional `EvaluationContext?` when their
implementation performs observable ECMAScript abstract operations.

Generated prototype method bindings must detect the
`(JsValue thisValue, IReadOnlyList<JsValue> args, EvaluationContext? context)`
shape, install a `SetInvokeWithContext` callback, and pass the active context
to the implementation. The ordinary fallback delegate may pass `null` for
internal non-context calls, but JavaScript invocation paths must use the active
context-aware dispatch.

For `Object.prototype.propertyIsEnumerable`, keep the spec order explicit:
perform `ToPropertyKey`/`ToPropertyName` on the argument before coercing the
receiver with `ToObject`, then inspect the receiver's own descriptor
enumerability. Do not introduce a separate symbol-key storage path for this
class; the existing property-key representation already carries symbol keys.

## Consequences

- Future built-in prototype methods that call observable abstract operations
  such as `ToPropertyKey`, `ToNumber`, `Get`, or callback invocation should
  either accept the active `EvaluationContext?` or prove the operation cannot
  observe JavaScript execution.
- Source-generator changes for prototype methods must keep context-aware
  dispatch aligned with standalone host functions; adding context to the C#
  signature without `SetInvokeWithContext` only fixes direct internal calls.
- Focused proof should include the exact Test262 fixture or owning method
  group, for this issue:
  `Name=Object_prototype_propertyIsEnumerable`.
- This ADR is caused by issue #1053 / PR #1286.
