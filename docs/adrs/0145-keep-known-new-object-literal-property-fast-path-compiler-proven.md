# ADR 0145: Keep known-new object literal property fast path compiler-proven

## Status

Accepted

## Context

Issue `autrun-dis251i1ddvc-f6f277664b` / PR #1941 selected
`objectcreation` from the repeated benchmark baseline because plain object
literals still trailed Jint after ADR 0106 moved default data-property storage
onto the implicit `JsObject` path:

```text
objectcreation  asynkron_ms=2847  jint_ms=777
objectcreation  asynkron_ms=1187  jint_ms=639
```

The focused CPU profile,
`rtk ./tools/profile objectcreation --cpu --calltree-depth 40 --calltree-width 40`,
showed the remaining owner under `ExecuteInstructionLoop` was not descriptor
allocation anymore. It was per-property duplicate bookkeeping:

```text
DefineObjectLiteralProperty
  DefineDefaultDataProperty
    TrackPropertyInsertion
```

The accepted slice added a compiler-carried proof for static object-literal
data properties that are known to be new at that program point. The expression
program runner uses that proof to call
`JsObject.DefineKnownNewDefaultDataProperty`, which skips duplicate-key checks
in ordinary object storage and insertion-order tracking. `JsObjectState` also
keeps small object insertion order list-first and creates the lookup set only
after the small-object threshold is crossed.

Final repeated focused measurements improved the selected profile to 737 ms,
678 ms, and 671 ms for Asynkron, roughly 38-43% faster than the warmed 1187 ms
baseline.

## Decision

Keep the known-new object-literal property fast path as a compiler-proven
optimization, not as a runtime assumption that all object-literal writes are
fresh.

The durable policy is:

1. only mark a static default data property as known-new when every earlier
   property name in the literal is statically known and the same name has not
   already appeared;
2. stop issuing known-new proofs after computed names, spreads, or other
   unknown key-producing members because later static names may overwrite keys
   created by those members;
3. exclude `__proto__` prototype mutation, accessors, methods, duplicate
   static names, and non-default property shapes from the known-new storage
   shortcut;
4. keep the semantic owner in `JsObject`: if implicit default storage is not
   currently valid, `DefineKnownNewDefaultDataProperty` must fall back to the
   ordinary default data-property path; and
5. keep insertion-order storage optimized for the common small-object case, but
   preserve duplicate suppression for all generic mutation paths.

## Consequences

- The hot object-literal path avoids repeated duplicate scans and lookup
  structures only when the expression compiler can prove the property is new.
- Computed/spread/prototype/accessor/method cases remain conservative, which
  preserves JavaScript overwrite order and observable property descriptor
  behavior.
- Future object-literal performance slices should extend the compile-time proof
  or the `JsObject` storage owner, not add evaluator-local assumptions about
  object literal freshness.
- Focused tests for duplicate keys, computed keys, spreads, accessors, methods,
  `__proto__`, enumeration order, and descriptor promotion are required before
  widening this fast path.

## Related

- `docs/adrs/0106-keep-object-literal-default-data-properties-implicit.md`
- `docs/adrs/0096-keep-static-object-key-bytecode-normalization-spec-owned.md`
- `docs/performance/objectcreation-known-new-property-fast-path.md`
- `.claude/rules/performance-profiling-guardrails.md`
