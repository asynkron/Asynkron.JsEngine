# ADR 0084: Keep TypedArray slice species result validation explicit

## Status

Accepted

## Context

Issue #1085 / PR #1319 fixed the focused Test262
`TypedArray_prototype_slice_BigInt` crash group.

`%TypedArray%.prototype.slice` creates its destination through the receiver's
species constructor before copying elements. That species boundary is
observable: the constructor or species getter can throw, detach or resize
buffers, or return a typed array with a different content type. The old slice
path validated the source up front, called `SpeciesCreate`, and then copied
through `SetValue` without validating the species-created destination or the
source/destination content-type relationship.

That made BigInt source arrays with a Number typed-array species result fall
into the wrong storage path, and it left detached or out-of-bounds destination
state to surface as incidental host behavior instead of a JavaScript
completion.

## Decision

Keep `%TypedArray%.prototype.slice` on the spec-shaped species-copy boundary:

- validate the receiver before computing the slice range;
- run `TypedArraySpeciesCreate` before destination validation so species
  getter/constructor abrupt completions propagate first;
- validate the species-created result for detached or out-of-bounds state
  before copying;
- reject Number/BigInt content-type mismatches before copying;
- re-check source and destination detached/out-of-bounds state inside the copy
  loop before reading or writing each element.

Do not repair this class by moving validation before species creation or by
letting `SetValue` discover content-type mismatch after partial copy work has
started.

## Consequences

- Future typed-array methods that create destinations through species must
  treat the created result as untrusted and validate it after the observable
  constructor boundary.
- Typed-array Number/BigInt content-type compatibility is a method-level
  invariant for copy operations, not a storage helper fallback.
- Focused proof should include a local species mismatch regression plus the
  owning Test262 method group, for this issue:
  `Name=TypedArray_prototype_slice_BigInt`.
- This ADR is caused by issue #1085 / PR #1319 and is enforced by
  `.claude/rules/ecmascript-abstract-operations.md`.
