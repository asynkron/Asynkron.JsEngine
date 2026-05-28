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
6. Prefix-based timeout policy is allowed only for a generated heavyweight
   fixture family with one semantic root, a current focused proof, path
   normalization coverage, default-timeout coverage for ordinary fixtures, and
   an ADR naming the accepted prefix.
7. When a crash report targets a fixture that already has an exact extended
   timeout, prove the focused row and the helper policy before editing runtime
   code. If the current row passes and the gap is missing helper coverage, add
   or update the path-normalized regression instead of widening timeout policy
   or changing the owner runtime.

## Why

Issue #771 / PR #948 fixed the `DecodeURIComponent` Test262 crash for
`built-ins/decodeURIComponent/S15.1.3.2_A2.5_T1.js` by giving that large
four-byte fixture an extended execution timeout. The review bounce happened
because the first helper matched only the bare path and missed the equivalent
`test/built-ins/...` shape that `Test262File.FileName` can report.

Future agents should treat Test262 path strings as harness inputs that need
normalization and direct regression coverage, not as stable literals that can be
matched in only one form.

Issue #2562 / PR #2567 repeated the same URI fixture pattern for
`built-ins/decodeURI/S15.1.3.1_A2.5_T1.js`. The current focused Release rows
passed under the existing exact extended timeout, so the delivery did not change
`GlobalHelper` or broaden harness policy. It added the missing helper
regression coverage for both bare and `test/`-prefixed path shapes so future
agents can classify the row as timeout-policy-owned before reopening the URI
decoder.

Issue #1058 / PR #1289 added the first accepted prefix-based timeout exception:
`built-ins/RegExp/CharacterClassEscapes/`. The affected fixtures are generated
RegExp character-class escape packs over large Unicode ranges, and the final
focused proof
`rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "Name=RegExp_CharacterClassEscapes"`
passed 24/24 after the harness-policy change. Future agents may not treat that
exception as permission to widen other Test262 directories without the same
proof shape and a new ADR.

Issue #1742 / PR #1767 added another exact fixture-specific timeout for
`built-ins/Function/prototype/toString/built-in-function-object.js`. The row
exercises `Function.prototype.toString` over the whole built-in function object
inventory and was classified as harness timeout/resource pressure, not a native
source display correctness bug. Keep this as an exact path override with both
bare and `test/`-prefixed path regression coverage; do not widen all
`Function.prototype.toString` fixtures or edit native function source metadata
without a fresh semantic failure.

Issue #1745 / PR #1769 reused the same exact-path policy for
`intl402/Locale/invalid-tag-throws.js`. The fixture stresses invalid-language-tag
validation enough to need the extended Test262 execution budget after focused
evidence classified the row as harness timeout/resource pressure. Keep this as
an exact path override with both bare and `test/`-prefixed path regression
coverage; do not widen all `Intl.Locale` fixtures or change BCP-47 validation
semantics unless a current proof reproduces a semantic failure outside the
harness budget.
