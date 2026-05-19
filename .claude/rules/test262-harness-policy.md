# Test262 Harness Policy

When changing Test262 harness behavior, keep fixture-specific policy exact and
path-normalized.

## Rules

1. Normalize the optional leading `test/` root before comparing
   `Test262File.FileName` or any path derived from it.
2. Keep file-specific overrides scoped to exact fixture paths. Do not replace an
   exact heavy-fixture timeout with a broad method-group or directory override
   without new proof and a new ADR.
3. Add regression coverage for both accepted path shapes when the helper can see
   either `built-ins/...` or `test/built-ins/...`.
4. Assert the default harness behavior for an ordinary nearby fixture so a
   special case cannot silently widen.
5. Prefer runtime fixes for compliance or allocation bugs; use per-fixture
   harness policy only when the issue is the harness limit around a known heavy
   upstream fixture.

## Why

Issue #771 / PR #948 fixed the `DecodeURIComponent` Test262 crash for
`built-ins/decodeURIComponent/S15.1.3.2_A2.5_T1.js` by giving that large
four-byte fixture an extended execution timeout. The review bounce happened
because the first helper matched only the bare path and missed the equivalent
`test/built-ins/...` shape that `Test262File.FileName` can report.

Future agents should treat Test262 path strings as harness inputs that need
normalization and direct regression coverage, not as stable literals that can be
matched in only one form.
