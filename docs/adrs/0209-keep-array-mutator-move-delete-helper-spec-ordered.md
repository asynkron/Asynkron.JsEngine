# ADR 0209: Keep array mutator move/delete helper spec-ordered

## Status

Accepted

## Context

Issue `autrun-dit384rttcps-5aec2213ed` / PR #2247 was a recurring
code-reduction child over `ArrayPrototype.Mutators.cs`.

`Array.prototype.shift`, `unshift`, and both shifting branches of `splice`
repeated the same element move/delete body: read the source indexed element,
write the target when the source exists, otherwise check whether the target
property exists and delete-or-throw at that target. The duplication was real,
but the surrounding loops were not interchangeable because each mutator owns
different bounds, direction, length updates, inserted elements, result arrays,
and trailing cleanup.

The accepted delivery added `MoveExistingElementOrDeleteTarget(...)` and
replaced only the repeated move/delete blocks. It did not reshape receiver
validation, reentrancy guards, loop ranges, `splice` result construction,
length writes, or inserted-element writes.

Focused proof used:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~Array"
```

The focused Array proof passed 513 tests. The carried build evidence recorded
`ArrayPrototype.Mutators.cs` moving from 525 to 506 C# code lines, and the
canonical `make quality` verification later passed the internal suite.

## Decision

Keep Array mutator indexed move/delete compaction in a small spec-ordered helper
when the operation is exactly identical.

For `shift`, `unshift`, `splice`, and future exact matches:

1. check the source element through `TryGetExistingElement(...)` first;
2. when the source exists, write the target through the same `SetProperty` path;
3. when the source is absent, check target presence with `HasProperty(...)`;
4. delete through `DeletePropertyOrThrow(...)` with the active object-like
   receiver, method name, and realm;
5. keep caller loop direction, bounds, length updates, result construction,
   insertion, and trailing deletion cleanup outside the helper; and
6. avoid delegate-based loop extraction or flag-shaped helpers unless the proof
   covers both JavaScript observability and hot-path costs.

## Consequences

- Sparse array holes keep their observable delete semantics instead of becoming
  `undefined` writes or no-ops.
- Future fixes to this exact move/delete operation have one owner, while each
  mutator still owns its distinct algorithm steps.
- Similar-looking code in other Array methods, such as `copyWithin`, should only
  reuse this helper after a focused proof shows the same source-check, target
  write, target-exists, and delete-or-throw contract applies.
- Code-reduction proof for this seam should include focused Array tests,
  `git diff --check`, and a code-size or duplication signal.

## Related

- Issue `autrun-dit384rttcps-5aec2213ed`
- PR #2247
- `.claude/rules/js-spec-property-access.md`
- `src/Asynkron.JsEngine/StdLib/Array/ArrayPrototype.Mutators.cs`
