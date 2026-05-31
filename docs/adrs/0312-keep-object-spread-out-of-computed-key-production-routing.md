# ADR 0312: Keep object spread out of computed-key production routing

## Status

Accepted

## Context

Delivery PR #2912 widened the production unified-bytecode literal boundary to
admit simple object spread entries such as:

```js
return { a: 1, ...source, b: 2 };
```

The review repair found a nearby routing hazard: an `ObjectSpread` operation can
also appear inside the payload that computes an ordinary computed property key,
for example:

```js
return box[{ ...source }];
```

That shape is not an object-literal entry owned by the unified-bytecode object
literal compiler. It is a computed-key expression whose evaluation,
property-key coercion, and side effects remain outside the admitted first
object-spread production boundary. If the selector only checks that
`ObjectSpread` has a simple preceding source operand, it can misclassify this
key-payload spread as an admitted object-literal spread.

## Decision

Admit object spread only when the operation belongs to a measured simple object
literal span, and keep object spread inside computed property-key payloads as a
pre-VM production decline.

- Eligibility must distinguish literal-entry spread from spread in computed-key
  payloads before generic property-read or unsupported-shape fallback.
- The computed-read key-payload bounds helper is the shared boundary detector
  for this class of hazard; do not duplicate a selector-side second recognizer
  that can drift from ordinary computed-read lowering.
- `box[{ ...source }]` and equivalent computed-key payload forms must decline as
  `ObjectLiteralOrSpreadDependency`, and public invocation coverage must prove
  the exact owning function body does not route through
  `unified-bytecode-production-fast-path`.
- Future object-literal widening must preserve this ownership split: admitting a
  spread syntax form in one expression context does not admit the same
  `ExpressionOpKind.ObjectSpread` wherever it appears in an `ExpressionProgram`.

## Consequences

- Simple object literals with spread entries can route through production
  unified bytecode without admitting arbitrary object-spread-bearing key
  expressions.
- Dependency pre-scans remain responsible for finding the most specific decline
  before later operations in the same payload trigger broader property-read or
  call/dynamic declines.
- Future computed-key widening needs selector, compiler, VM, and public
  invocation proof in the same slice before object-spread payloads in keys can
  move out of this decline bucket.

## Evidence

- Delivery PR #2912 merged as commit `6edc742ee`:
  `Agent: task planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-e62d6987e4`.
- Review repair commit `17c4e0b58` kept `ObjectSpread` out of computed-key
  routing by sharing the computed-read key-payload bounds helper and requiring
  object spread to appear inside a measured simple object literal span.
- Focused regression verification from the carried build summary passed 23
  tests.
- The unified-bytecode production/prototype/invocation proof pack passed 655
  tests.
- `rtk git diff --check` passed.
- The focused AST seam scan for `EvaluateExpression(` /
  `ProfileEvaluateExpression(` in runner files found no matches.

## Related

- `docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`
- `docs/adrs/0290-admit-array-and-object-literals-in-unified-bytecode-simple-span-measurement.md`
- `docs/adrs/0291-admit-simple-computed-keys-in-unified-bytecode-span-scanner.md`
- `docs/adrs/0311-admit-optional-named-computed-read-continuations-in-unified-bytecode.md`
- `docs/rules/unified-bytecode-prototypes.md`
