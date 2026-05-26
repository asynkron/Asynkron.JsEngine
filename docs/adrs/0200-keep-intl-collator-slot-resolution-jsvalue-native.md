# ADR 0200: Keep Intl.Collator slot resolution JsValue-native

## Status

Accepted

## Context

Issue #2202 / PR #2207 continued the ADR 0196 Intl brand-validation cleanup by
targeting the `Intl.Collator` prototype owner surface.

ADR 0196 removed the shared `IntlBrandExtensions.EnsureBrand(this object? ...)`
overload and established that private Intl receiver brand checks should stay
`JsValue`-native. After that shared helper cleanup, `IntlCollatorPrototype`
still kept a local two-step carrier path: prototype entrypoints passed a
`JsValue` receiver to `ValidateCollatorReceiver(...)`, that helper returned a
`JsObject`, and `GetSlots(JsObject collator)` then read the private Collator
slot payload.

That path did not change public behavior, but it kept the Collator owner
surface organized around a private object carrier immediately after a
`JsValue` brand check. Future work could have copied or extended that shape and
quietly reintroduced the same receiver-carrier split that ADR 0196 was meant to
remove.

The accepted delivery changed `get compare` and `resolvedOptions` to call a
single `GetSlots(JsValue thisValue)` helper. That helper performs
`thisValue.EnsureBrand(CollatorBrand, Realm, ...)` and then reads the existing
internal slot property from the branded object. The bound compare function and
slot storage remained unchanged.

Focused proof used:

```bash
rtk rg -n "ValidateCollatorReceiver|GetSlots\(JsObject" src/Asynkron.JsEngine/StdLib/Intl/IntlCollatorPrototype.cs
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~IntlSupportedValuesTests.CollatorPrototypeBorrowedMethods"
```

The retired-helper search returned no matches, and the focused borrowed-method
regressions passed for valid branded Collator receivers and incompatible
primitive/plain-object receivers.

## Decision

Keep `Intl.Collator` prototype receiver validation and slot resolution
`JsValue`-native inside the owner surface.

For future `Intl.Collator` and closely related Intl owner-surface cleanup:

1. call the slot helper from prototype methods with the original `JsValue`
   receiver;
2. keep brand validation and internal-slot extraction in one owner-local helper
   when the caller already has a `JsValue`;
3. do not split that helper into `Validate*Receiver(...)` returning `JsObject`
   plus a second `GetSlots(JsObject ...)` carrier hop;
4. preserve bound-function closures that capture internal slots, especially
   `Intl.Collator.prototype.compare`; and
5. prove the slice with a scoped retired-helper search plus borrowed prototype
   method tests covering both branded receivers and incompatible receivers.

## Consequences

- `Intl.Collator` no longer has a private object-carrier slot-resolution path
  after the `JsValue` brand check.
- Borrowed `compare` getter and `resolvedOptions` calls keep their existing
  branded-receiver behavior and incompatible-receiver TypeError behavior.
- ADR 0196 remains the shared Intl brand-validation policy; this ADR records
  the Collator-specific owner-surface follow-through so future cleanup checks
  local prototype helpers as well as shared extension overloads.
- Other Intl prototypes should not be changed by analogy without their own
  focused owner-surface search and semantic proof.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0196-keep-intl-receiver-brand-validation-jsvalue-native.md`
