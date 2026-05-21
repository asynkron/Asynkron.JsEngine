# URI Percent-Decoding

When changing URI percent-decoding, keep `decodeURI` and
`decodeURIComponent` aligned through the shared decoder and prove both surfaces.

## Rules

1. Validate percent escapes and continuation-byte bounds before indexing into
   the source string. Malformed or truncated input must throw `URIError`, not a
   host exception.
2. Preserve the `decodeURI` reserved single-byte escape rule: reserved escaped
   characters stay escaped for `decodeURI`, while `decodeURIComponent` decodes
   them through the shared helper.
3. Validate one-byte fast paths with the same UTF-8 sequence-length rules as the
   general decoder. `%80`, `%FF`, and incomplete multi-byte leads such as `%C2`
   must throw `URIError`; they are not legal standalone scalar decodes.
4. Decode valid UTF-8 directly, including four-byte supplementary Unicode
   scalars as surrogate pairs. Do not reintroduce per-sequence `byte[]`,
   `Substring`, or text-encoding round-trip allocations in the hot percent
   decoder path without a current focused proof.
5. Reject overlong encodings, surrogate-range scalars, out-of-range scalars,
   malformed continuation bytes, and truncated percent sequences through the
   same `URIError` path.
6. Prove shared-decoder changes with both focused Test262 method groups:
   `Name=DecodeURI` and `Name=DecodeURIComponent`, using
   `xUnit.MaxParallelThreads=1 -timeout 20000`.

## Why

Issue #770 / PR #940 fixed a `DecodeURI` Test262 crash in
`built-ins/decodeURI/S15.1.3.1_A2.5_T1.js`. The root lesson was not just that
four-byte UTF-8 needed a missing branch: the decoder also had to avoid
per-decoded-sequence allocations in a large RFC 3629 loop while preserving
shared `decodeURI` / `decodeURIComponent` malformed-sequence behavior.

The first focused proof run during the fix exposed a concrete recurrence risk:
the new continuation parser could be called past the end of the input unless
bounds were checked before indexing. Future agents should keep validation order,
reserved-character policy, and allocation stability together instead of fixing
one URI edge in isolation.

Issue #1379 / PR #1383 optimized the same shared decoder for the four-byte
`DecodeURI` and `DecodeURIComponent` Test262 fixtures by adding a single-scalar
fast path. Review caught that the initial fast path could accidentally accept
illegal standalone bytes (`%80`, `%FF`) or incomplete multi-byte leads (`%C2`).
The durable rule is that URI fast paths may be allocation-oriented, but they
must remain behavior-equivalent to the general decoder before returning.
