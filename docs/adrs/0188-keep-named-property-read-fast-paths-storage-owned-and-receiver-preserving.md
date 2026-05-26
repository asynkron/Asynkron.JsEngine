# ADR 0188: Keep named property read fast paths storage-owned and receiver-preserving

## Status

Accepted

## Context

Issue `autrun-disqa6r1nle8-d1809e0cbe` / PR #2157 optimized the recurring
optimizer `propertyaccess` profile. The selected workload repeatedly reads
direct and nested object properties inside loop-local `sum += ...`
accumulation.

The build-stage baseline from `rtk ./benchmark.sh` showed:

```text
propertyaccess  1735 ms  576 ms  Jint 3.01x faster
```

The CPU profile for:

```bash
rtk ./tools/profile propertyaccess --cpu --calltree-depth 40 --calltree-width 40
```

named this hot subtree:

```text
HandleCompoundAssignmentSlot
-> EvaluateExpressionProgram
-> GetProgramNamedPropertyValue
-> JsOps.TryGetPropertyValue
-> JsObject.TryGetProperty*
```

The tempting broad fixes were a property cache, a source-level member-expression
rewrite, or a generic bypass of object lookup. Those would be too wide for this
runtime because property reads are observable through accessors, prototype
lookup, virtual providers, proxy-like object carriers, primitive prototypes,
receiver binding, and JavaScript throw state.

The accepted delivery kept the optimization local to already-owned runtime
boundaries:

1. `JsOps.TryGetPropertyValue` now sends `JsValueKind.Object` values whose
   payload is an ordinary `JsObject` directly to
   `JsObject.TryGetProperty(propertyName, target, context, out value)`, keeping
   the original `JsValue` receiver.
2. `JsObject.TryGetOwnPropertyJsValue` may return stored own data-property
   values directly when no accessor descriptor owns that name. Accessors,
   virtual properties, descriptor materialization, and prototype traversal stay
   on the existing semantic path.
3. `HandleCompoundAssignmentSlot` has a flat-slot `+= <expression>` path for
   non-awaiting add compound assignments where the target slot is proven and
   the RHS is an expression program, so the loop accumulator can avoid generic
   identifier assignment while still evaluating the RHS once and using the
   shared addition fallback when the profiled direct add helper cannot produce a
   result.

Focused proof covered own data-property shadowing of a prototype getter,
prototype getter receiver binding, and single getter evaluation for `sum +=
obj.value`. The build-stage final signal repeated selected-profile timing at
`1067 ms`, `1262 ms`, and `1061 ms`, clearing the 10 percent threshold against
the `1735 ms` baseline. The focused semantic pack plus `GetPropertyNameTests`
passed 14 tests, `git diff --check` passed, the AST-eval seam scan found no
matches, and `rtk ./tools/profile forloop --memory` reported `6.72 MB`.

## Decision

Keep named-property read fast paths at the runtime property-access and object
storage boundaries. Do not turn a successful `propertyaccess` slice into a
generic property cache, a source-shape rewrite, or an evaluator-local shortcut
that assumes ordinary data properties without consulting the storage owner.

For future named-property performance work:

1. `JsOps` may bypass generic object-carrier dispatch only for runtime values
   whose concrete payload and receiver semantics are already known, such as an
   ordinary `JsObject` carried inside `JsValueKind.Object`.
2. Keep the original `JsValue` receiver through context-aware reads so inherited
   accessors observe the right `this` value.
3. Let `JsObject` own direct stored-value shortcuts. A direct own-value return
   is valid only for stored data properties that are not accessor descriptors.
4. Fall back for accessors, virtual properties, prototype traversal,
   non-`JsObject` property accessors, primitive prototype reads, private-field
   boundaries, and any context that needs JavaScript throw propagation.
5. Compound-add slot shortcuts that evaluate a property RHS must preserve the
   ECMAScript read/update/write order: read the left slot, evaluate the RHS
   exactly once, propagate pending awaits or throws through the existing runner
   paths, compute the compound operator, then write the proven slot.
6. Prove retained changes with repeated selected-profile timing and focused
   semantic tests for own data shadowing, prototype getter receiver binding, and
   getter evaluation count.

## Consequences

- The propertyaccess win remains storage-owned and receiver-preserving instead
  of becoming a global cache invalidation problem.
- Future `JsOps`/`JsObject` changes can reuse the direct ordinary-object path,
  but they must keep the generic fallback as the semantic owner for any shape
  that is not proven ordinary data storage.
- Compound assignment runner work should treat this as a proven flat-slot
  accumulator path, not as permission to bypass assignment-reference semantics
  in dynamic lookup, `with`, or proxy-observable contexts.
- The performance note
  `docs/performance/propertyaccess-compound-add-fast-path.md` remains the
  detailed measurement record; this ADR owns the durable architectural boundary.

## Related

- `.claude/rules/performance-profiling-guardrails.md`
- `.claude/rules/js-spec-property-access.md`
- `.claude/rules/expression-bytecode-assignment.md`
- `docs/adrs/0148-keep-context-property-reads-jsvalue-receiver-native.md`
- `docs/adrs/0107-keep-self-referential-assignment-slot-optimization-slot-proven.md`
