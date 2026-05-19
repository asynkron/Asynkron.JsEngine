# ADR 0020: Keep JSON.parse number lexeme access targeted

## Status

Accepted

## Context

Issue #792 / PR #982 fixed the Test262 `JSON_parse` failures for
`built-ins/JSON/parse/text-negative-zero.js`. `System.Text.Json.JsonElement`
correctly parses JSON number grammar, but `GetDouble()` alone cannot tell
whether a zero-valued token was written as `-0`, `-0.0`, or `-0e0`.
ECMAScript exposes that distinction: `JSON.parse("-0")` must produce negative
zero, and the reviver path must preserve both the parsed value and
`context.source`.

The first repair used the raw JSON token text for all numeric values. Review
correctly called out that `JsonElement.GetRawText()` materializes a string, so
calling it for every number would add per-number allocations to the common
`JSON.parse` path. The final delivery kept lexeme access only where semantics
need it: zero-valued numbers for signed-zero detection, and reviver source
tracking when `context.source` is observable.

## Decision

Keep `JSON.parse` number materialization on the parsed `double` fast path, and
read the raw JSON number token only when required by observable ECMAScript
semantics.

The JSON number path must:

- construct the JavaScript number from `JsonElement.GetDouble()` for ordinary
  nonzero numbers;
- inspect `GetRawText()` for zero-valued numbers so negative-zero spellings
  create `new JsValue(-0.0d)`;
- reuse the same raw text for reviver source tracking when it has already been
  materialized;
- keep reviver `context.source` behavior intact without forcing raw-text
  allocation on every default parse number;
- prove signed-zero behavior with reciprocal-infinity or SameValue-style
  assertions, not ordinary numeric equality.

## Consequences

- Future JSON number work should treat the original token lexeme as semantic
  data only for cases that can observe it, especially signed zero and reviver
  source tracking.
- Broad JSON parser rewrites are not needed for this class of issue; keep
  `System.Text.Json` grammar ownership unless a separate spec gap proves
  otherwise.
- Allocation behavior is part of the contract for large JSON inputs. Adding
  unconditional `GetRawText()`, substring, or source-string allocation to the
  default numeric parse path needs fresh review and targeted proof.
- This ADR is caused by issue #792 / PR #982 and complements the root
  `.claude/rules/ecmascript-numeric-coercions.md` rule for signed-zero numeric
  boundaries.
