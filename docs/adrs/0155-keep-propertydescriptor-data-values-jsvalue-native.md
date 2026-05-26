# ADR 0155: Keep PropertyDescriptor data values JsValue-native

## Status

Accepted

## Context

Issue `autrun-discmtujl4ko-18deddaa26` / PR #2008 continued the bounded
object-carrier cleanup for `PropertyDescriptor` data descriptors.

The delivered slice migrated descriptor initializers in
`src/Asynkron.JsEngine/Runtime/JsOps.cs`,
`src/Asynkron.JsEngine/Runtime/Prototypes/JsPrototype.cs`, and
`src/Asynkron.JsEngine/Ast/JsArrayExtensions.cs` from the legacy `Value`
compatibility setter to the typed `JsValue` setter. These descriptors were
constructing JavaScript data properties from values the core runtime already
held as `JsValue` or known JavaScript objects, so routing through
`PropertyDescriptor.Value` only preserved an avoidable
`JsValue.FromObjectUnsafe(...)` bridge.

The delivery also exposed a boundedness decision: making the compatibility
setter error-level obsolete was not appropriate in this slice. Remaining
repo-wide `Value` callsites, including generated-code surfaces, meant strict
obsoletion would have changed the scope from a focused descriptor migration
into a repository-wide closeout.

Issue `autrun-disdwrowfjf4-2cfa8decbc` / PR #2024 later applied the same
decision to `StandardLibrary.DefineConstantProperty`. The first build changed
the helper parameter and fallback setter to `JsValue`, but review caught that
the `PropertyDescriptor` initializer still used `Value = value`. Because the
compatibility setter delegates to `JsValue.FromObjectUnsafe(value)`, the helper
still had an object-carrier sink even though the signature compiled cleanly.

## Decision

Keep core `PropertyDescriptor` data values on the `JsValue` setter whenever the
descriptor is carrying a JavaScript value inside runtime, AST, prototype, or
standard-library code.

For descriptor cleanup slices:

1. assign `PropertyDescriptor.JsValue` directly when the source value is already
   a `JsValue`;
2. wrap known JavaScript object instances explicitly, for example with
   `JsValue.FromJsArray(...)`, instead of passing them through `Value`;
3. treat `PropertyDescriptor.Value` as a compatibility bridge, not the normal
   core-runtime data descriptor path;
4. prove each bounded migration with a before/after search for legacy
   descriptor setters such as `\bValue\s*=` in the selected file set, including
   helper bodies when the migration changes a helper signature; and
5. defer `[Obsolete(..., true)]` on the compatibility setter unless the selected
   work explicitly owns the full repository-wide and generated-code migration.

## Consequences

- Future descriptor migrations should be small, file-set scoped, and evidenced
  by the legacy-setter search plus the focused semantic proof for the owning
  descriptor cluster.
- Strict obsolete pressure on `PropertyDescriptor.Value` is a separate closeout
  task. It should not be mixed into narrow cleanup issues unless the issue body
  explicitly accepts the full caller migration.
- Generated-code or broad standard-library callsite exposure is a scope signal,
  not a reason to abandon typed descriptor migration.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0106-keep-object-literal-default-data-properties-implicit.md`
- `docs/adrs/0145-keep-known-new-object-literal-property-fast-path-compiler-proven.md`
