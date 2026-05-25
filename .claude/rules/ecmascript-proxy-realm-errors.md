# ECMAScript Proxy Realm Errors

When changing proxy internal methods, keep error realm ownership tied to the
specific operation being implemented.

## Rules

1. For revoked proxy `[[Call]]` and `[[Construct]]` null-handler checks, throw
   with the current execution realm when one exists, falling back to the proxy
   creation realm only when there is no current realm. In `JsProxy`, use the
   standard `ErrorRealm` shape (`RealmState.Current ?? _realm`) for these
   checks.
2. Do not reintroduce a generic `RevokedProxyErrorRealm` helper that prefers
   the proxy creation realm for apply/construct. A proxy-creation-realm-first
   helper is valid only for a separately proven proxy operation with its own
   spec/test evidence.
3. Keep proxy realm proofs operation-specific. Name the internal method
   (`[[Call]]`, `[[Construct]]`, property trap, Reflect helper, etc.) and run
   the focused Test262 group that exercises that operation before widening.
4. Do not let tail-call, Reflect, or host-call routing override the realm
   selected by the proxy operation. Routing layers preserve the proxy result or
   abrupt completion; they do not choose a replacement realm.

## Why

Issue #1864 / PR #1890 fixed residual `Proxy_apply` and `Proxy_construct`
Test262 failures after revoked callable/constructable proxies created in one
realm were applied or constructed from another realm. The previous
`RevokedProxyErrorRealm` helper preferred the proxy creation realm, but these
null-handler `TypeError` objects must come from the current execution realm
when present. This also refined older proper-tail-call guidance from issue
#1748 / PR #1796: preserve the proxy operation's selected realm, not a blanket
"proxy realm" rule.

Focused proof:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "FullyQualifiedName~Proxy_apply|FullyQualifiedName~Proxy_construct"
```

Related ADRs:

- `docs/adrs/0137-keep-revoked-proxy-apply-construct-errors-current-realm.md`
- `docs/adrs/0126-keep-proper-tail-calls-runtime-context-owned.md`
