# ADR 0137: Keep revoked proxy apply/construct errors current-realm

## Status

Accepted

## Context

Issue #1864 / PR #1890 fixed residual Test262 failures in the
`Proxy_apply` and `Proxy_construct` groups. The failing cases created a
revoked callable or constructable proxy in one realm, then performed
`[[Call]]` or `[[Construct]]` from another current execution realm. The
observable assertion was the realm identity of the thrown `TypeError`.

`JsProxy` already had a general `ErrorRealm` helper that prefers
`RealmState.Current` and falls back to the proxy's creation realm. The revoked
apply/construct checks had drifted to a separate `RevokedProxyErrorRealm`
helper that preferred the proxy creation realm first. That made the
null-handler `TypeError` come from the wrong realm for the Test262 rows.

The nearby proper-tail-call learning from issue #1748 had recorded a broader
"revoked proxy throws from the proxy realm" reminder. Issue #1864 narrowed that
guidance: tail-call routing must preserve the operation-selected proxy error
realm, but for revoked proxy `[[Call]]` and `[[Construct]]` null-handler
checks, the selected realm is the current execution realm when one exists.

## Decision

Keep revoked proxy `[[Call]]` and `[[Construct]]` null-handler errors on the
standard proxy error path: `RealmState.Current ?? _realm`.

Do not introduce a separate proxy-creation-realm-first helper for apply or
construct revoked-handler checks. If a future proxy operation has different
realm ownership, document and prove that operation separately rather than
sharing a generic "revoked proxy realm" helper across all traps and internal
methods.

Keep proof scoped to the operation under repair. The focused Test262 proof for
this class is:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "FullyQualifiedName~Proxy_apply|FullyQualifiedName~Proxy_construct"
```

## Consequences

- Cross-realm apply/construct revoked proxy failures create `TypeError`
  objects from the active execution realm, matching the Test262 realm identity
  assertions.
- Proxy realm guidance must be operation-specific. A tail-call, construct,
  apply, property trap, or Reflect helper may preserve different realm
  ownership, so future fixes should name the internal method and exact proof
  filter.
- The old `RevokedProxyErrorRealm` helper should stay deleted unless a future
  operation proves a proxy-creation-realm-first revoked check is required.
- Focused proxy Test262 filters are enough for this learn artifact; this
  docs-only pass does not require rerunning broad Test262.

## Related

- `.claude/rules/ecmascript-proxy-realm-errors.md`
- `.claude/rules/proper-tail-calls.md`
- `docs/adrs/0126-keep-proper-tail-calls-runtime-context-owned.md`
