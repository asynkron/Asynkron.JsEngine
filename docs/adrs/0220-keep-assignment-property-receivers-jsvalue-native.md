# ADR 0220: Keep assignment property receivers JsValue-native

## Status

Accepted

## Context

Issue `autrun-dit8gewlbljs-23ab36dc9e` / PR #2305 continued the recurring
object-to-`JsValue` cleanup by targeting the private object-property assignment
receiver path.

Before the delivery, `AssignmentReferenceResolver.AssignObjectProperty(...)`
accepted `object? receiver = null` and normalized it with
`JsValue.FromObjectUnsafe(receiver)`. The main property-assignment callsite in
`AssignmentReferenceHelpers` already had both the original `target` `JsValue`
and the extracted `JsObject`; it passed the extracted object as the receiver.
That kept an avoidable object-carrier bridge in the generic assignment write
path and risked making future setter/proxy work treat receiver identity as an
implementation detail instead of the original JavaScript value.

The accepted delivery migrated the receiver parameter to `JsValue?`, kept the
default behavior by using `target` when no receiver is supplied, and changed the
core assignment callsite to pass the original `target` `JsValue` as the
receiver.

Focused proof used:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Release --filter "FullyQualifiedName~AssignmentReferenceTests"
rtk rg -n "object\? receiver|JsValue\? receiver|AssignObjectProperty\(" src/Asynkron.JsEngine/Ast/AssignmentReferenceResolver.cs src/Asynkron.JsEngine/Ast/AssignmentReferenceHelpers.cs
```

The focused test pack passed 11/11. The targeted legacy signature signal moved
from one `object? receiver` occurrence in the selected assignment slice to zero.

## Decision

Keep private assignment property-write receivers `JsValue`-native when the
caller already has the JavaScript receiver value.

For `AssignmentReferenceResolver`, `AssignmentReferenceHelpers`, and adjacent
assignment-property write helpers:

1. prefer helper signatures that accept `JsValue receiver` or nullable
   `JsValue? receiver` with an explicit default to the assignment target;
2. pass the original `JsValue` receiver from callsites that already have it,
   rather than an extracted `JsObject` payload;
3. keep any unavoidable legacy conversion explicit at the remaining legacy
   branch with `JsValue.FromObjectUnsafe(...)`; and
4. prove each migration with a focused before/after search for the retired
   `object? receiver` shape plus assignment-reference tests that cover the
   affected write semantics.

## Consequences

- The generic object-property assignment helper no longer normalizes its
  receiver through an untyped object-carrier parameter.
- Accessor and proxy receiver identity stays anchored to the original
  JavaScript `JsValue` supplied by the assignment reference.
- Future assignment cleanup should not reintroduce `object? receiver`
  convenience parameters in private runtime write paths just because the target
  object has already been extracted for descriptor work.
- This ADR is caused by issue `autrun-dit8gewlbljs-23ab36dc9e` / PR #2305.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0148-keep-context-property-reads-jsvalue-receiver-native.md`
- `docs/adrs/0203-keep-runner-symbol-stores-jsvalue-native.md`
