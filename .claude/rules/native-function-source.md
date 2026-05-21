# Native Function Source Metadata

When implementing or modifying host functions that participate in
`Function.prototype.toString`, keep native source display metadata immutable and
creation-time stamped.

## Rules

1. Prefer exact `ICallableMetadata.SourceReference` text for user-authored
   functions before using any native-source fallback.
2. For `HostFunction` built-ins, render native source from private
   creation-time metadata set by the creator or source generator.
3. Do not derive native source display names from the JavaScript-visible `name`
   property. User code can redefine that property and must not be able to forge
   built-in native source text.
4. Validate display names before rendering. Identifier-like names, `get` / `set`
   accessor names, and single-pair bracketed symbol names are allowed; malformed
   bracketed forms such as `[a]]` must fall back to anonymous native source.
5. When adding generated built-ins, accessors, symbol members, constructor
   members, globals, or compatibility stubs, stamp the native display metadata
   at `HostFunction` creation time.
6. When cloning or snapshotting `HostFunction` instances for realm reuse, copy
   the private native display metadata explicitly. Cloning the JavaScript
   properties object is not enough because this metadata is deliberately not a
   mutable observable property.
7. Add focused regression coverage for both mutable `name` forgery and malformed
   bracketed native names when touching this area.

## Why

Issue #788 / PR #963 fixed Test262 `Function_prototype_toString` failures by
teaching `HostFunction` to render NativeFunction-shaped source. The review
blocker showed that using the public `name` property was observable and mutable:
user code could redefine it and forge built-in source strings. The accepted fix
stores private creation-time display metadata, stamps it from generated
built-in creation sites, and rejects malformed bracketed names before rendering.

Issue #1378 / PR #1380 showed the same invariant applies to engine-owned clone
paths: `BaseRealmSnapshot` created replacement `HostFunction` objects for
Test262 base-realm reuse and dropped the private display metadata until the
clone copied it explicitly.

Related ADR: `docs/adrs/0014-keep-native-function-source-display-metadata-immutable.md`.
