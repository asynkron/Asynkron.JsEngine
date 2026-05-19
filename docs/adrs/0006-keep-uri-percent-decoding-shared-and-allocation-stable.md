# ADR 0006: Keep URI percent-decoding shared and allocation-stable

## Status

Accepted

## Context

Issue #770 fixed the Test262 `DecodeURI` crash for
`built-ins/decodeURI/S15.1.3.1_A2.5_T1.js`. The case iterates all valid RFC
3629 four-byte percent-encoded URI sequences and expects `decodeURI` to return
the corresponding JavaScript supplementary-code-point string.

Before PR #940, `DecodeUri` decoded percent sequences through per-sequence
`byte[]` and `Substring` allocations. That made the large Test262 loop fragile
and left the four-byte path exposed to crash-style failures. The same helper is
used by both `decodeURI` and `decodeURIComponent`, while `decodeURI` has the
additional rule that reserved single-byte escapes stay escaped.

The delivery also found a concrete validation hazard while proving the fix:
continuation-byte parsing must check bounds before indexing so malformed or
truncated percent sequences continue to throw `URIError` rather than leaking a
host `IndexOutOfRangeException`.

## Decision

Keep URI percent-decoding in the shared `DecodeUri` helper, but decode UTF-8
percent sequences directly without allocating per decoded sequence.

The helper must:

- parse each percent byte through direct hex-nibble validation before indexing
  continuation positions;
- preserve `decodeURI` reserved single-byte escape behavior;
- decode valid multi-byte UTF-8, including four-byte supplementary scalars, into
  JavaScript strings by appending surrogate pairs where required;
- route malformed, truncated, overlong, surrogate-range, and out-of-range UTF-8
  sequences to `URIError`;
- prove changes with the focused `DecodeURI` Test262 method group and the
  shared-decoder `DecodeURIComponent` method group before any broader run.

## Consequences

- Future URI decoder fixes should treat `decodeURI` and `decodeURIComponent` as
  one shared compliance surface with different reserved-character policy.
- Allocation avoidance is part of correctness for large URI Test262 loops:
  returning to per-sequence `byte[]`, `Substring`, or encoding round-trips in
  the hot decoder path needs fresh proof that the loop remains stable.
- Bounds checks must happen before every continuation-byte read so malformed
  input stays ECMAScript-visible as `URIError`.
- This ADR is caused by issue #770 / PR #940 and complements the root
  `.claude/rules/uri-percent-decoding.md` rule for future implementation work.
