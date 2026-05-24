# ADR 0106: Keep object literal default data properties implicit

## Status

Accepted

## Context

Issue `autrun-diqzaewea814-77aafdbdd9` / PR #1692 selected
`objectcreation` from the required `rtk ./benchmark.sh` baseline because it was
a current high-gap Asynkron-vs-Jint loss:

```text
objectcreation  asynkron_ms=1464  jint_ms=455  Jint 3.22x faster
```

The required CPU profile,
`rtk ./tools/profile objectcreation --cpu --calltree-depth 40 --calltree-width 40`,
showed the selected workload spending most of its object-literal creation time
under ordinary property definition:

```text
DefineObjectLiteralProperty                       116.10 ms
JsObject.DefineProperty / DefinePropertyInternal  116.10 ms
Dictionary<__Canon,__Canon>.set_Item              113.32 ms
```

The hot script repeatedly creates plain object literals with default data
properties such as `{ id, name, value, nested }`. These properties have the
ordinary object-literal attributes `writable`, `enumerable`, and `configurable`
all set to `true`, so allocating and storing a full descriptor for each fresh
key was avoidable in the common ordinary-object path.

The accepted implementation added `JsObject.DefineDefaultDataProperty` and
routed static and computed object-literal data properties through it. The helper
stores the value directly in ordinary object storage and preserves insertion
order when the target object is extensible, has no virtual property provider,
the key is not private, and no explicit descriptor already exists. Other shapes
still use the full descriptor machinery.

Repeated focused final runs improved Asynkron from the 1464 ms baseline to
1156 ms, 1229 ms, and 1169 ms, for roughly a 19% average improvement.

## Decision

Keep ordinary object-literal default data-property creation as an implicit
storage fast path owned by `JsObject`, not as ad hoc descriptor bypasses in
callers.

The durable policy is:

1. use the implicit storage path only for fresh ordinary default data properties
   whose observable descriptor is exactly writable/enumerable/configurable;
2. keep non-extensible objects, virtual providers, private fields, existing
   explicit descriptors, accessors, methods, and prototype mutation on the
   existing descriptor path;
3. materialize the default descriptor from storage for descriptor-reading
   operations such as `Object.getOwnPropertyDescriptor`; and
4. require any future object-literal property fast path to carry both current
   profile evidence and focused semantic proof for descriptor visibility,
   insertion order, extensibility, and later `Object.defineProperty` promotion.

## Consequences

- Plain object literals avoid descriptor allocation and descriptor-dictionary
  churn in the common `objectcreation` benchmark path.
- The descriptor dictionary remains the source of truth for observable
  non-default attributes and accessor/private/virtual property behavior.
- Future object-literal performance work should extend the `JsObject` storage
  owner when it can preserve these boundaries, rather than special-casing
  individual evaluator or bytecode callsites.
- A performance win alone is not sufficient in this area; the fast path must
  prove that implicit storage is still indistinguishable from an ordinary
  default data property at JavaScript observation points.

## Related

- `docs/performance/objectcreation-default-data-property-storage.md`
- `.claude/rules/performance-profiling-guardrails.md`
