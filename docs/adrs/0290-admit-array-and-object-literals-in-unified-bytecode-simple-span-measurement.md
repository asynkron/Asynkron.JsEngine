# ADR 0290: Admit array and object literals in unified bytecode, simple span measurement

## Status

Accepted

## Context

Issue #2705 widened production unified-bytecode routing to admit **simple array
and object literals** as call arguments and as the RHS of named-property binary
expressions — e.g., `fn([a, b])`, `fn({x: 1, y: 2})`, and `obj.prop === [a, b]`.

Before this slice, `HasSimpleCallArguments` enforced a strict 1-op-per-argument
invariant (`callIndex - argsStartIndex == call.ArgumentCount`), and
`TryIsFirstBoundaryPropertyReadBinaryExpressionCandidate` checked the RHS as a
single op via `IsSimpleOperand`. Array and object literals are multi-op sequences
in the ExpressionProgram (`CreateArray` + N×`[element, ArrayPush]`; `CreateObject`
+ N×`[value, DefineObjectProperty]`), so both helpers rejected them.

The admitted call boundary before this slice covered:
- Scalar activation-resolved identifier, literal, `this`, `new.target` arguments
- Identifier and named/computed member-call shapes
- Spread calls (gh2676, ADR 0287)
- Optional calls (gh2689, ADR 0289)
- Non-spread construct calls (gh2690, ADR 0286)

## Decision

Introduce two span-measurement helpers and update two admission sites:

### `TryMeasureSimpleArrayLiteralSpan`

Validates `CreateArray` followed by N×`[simple-operand, ArrayPush]` (N ≥ 0) and
returns the total span length. Declines on the first `ArrayPushHole` (holey
arrays are not admitted) or any non-`ArrayPush` push op after an element.

### `TryMeasureSimpleObjectLiteralSpan`

Validates `CreateObject` followed by N×`[simple-operand, DefineObjectProperty]`
(N ≥ 0) and returns the total span length. Declines when:
- The value op is not a simple operand
- The define op is not `DefineObjectProperty` (computed keys use
  `DefineComputedObjectProperty` → declined)
- The property name is private (`IsPrivateName()`)
- `AllowNameInference` is set (function-literal value that would receive a name)

### `HasSimpleCallArguments` (span-walk replacement)

The existing `callIndex - argsStartIndex != call.ArgumentCount` pre-check is
replaced with a span-walk that consumes one logical argument at a time — either a
single simple op or a measured array/object literal span. The logical argument
count is incremented for each consumed unit and verified against
`call.ArgumentCount` at the end.

### `TryIsFirstBoundaryPropertyReadBinaryExpressionCandidate` (multi-op RHS)

The method previously assumed the RHS was a single op at `OperationCount - 2`.
The updated version walks the `GetNamedProperty` chain forward until the chain
ends, identifies `rhsStart`, and accepts the RHS if:
- It is a single simple operand (`rhsStart == rhsEnd`), or
- It is a simple array literal span that exactly fills `[rhsStart..rhsEnd]`, or
- It is a simple object literal span that exactly fills `[rhsStart..rhsEnd]`.

## Constraints (decline families preserved)

| Shape | Op | Decline reason |
|---|---|---|
| Holey array `[1,,3]` | `ArrayPushHole` | Not admitted by span measurement |
| Spread element `[...xs]` | `ArraySpread` | Per-op scan declines before admission site |
| Object method `{fn() {}}` | `DefineObjectMethod` | Per-op scan declines before admission site |
| Object spread `{...o}` | `ObjectSpread` | Per-op scan declines before admission site |
| Function value `{x: ()=>{}}` | `LoadFunctionLiteral` | Per-op scan declines before admission site |
| Name inference `{x: fn}` | `DefineObjectProperty(AllowNameInference=true)` | Span measurement declines |
| Private name `{#x: v}` | `DefineObjectProperty(IsPrivateName)` | Span measurement declines |
| Computed key `{[k]: v}` | `DefineComputedObjectProperty` | Span measurement declines |
| Nested literal `[[a, b]]` | inner `CreateArray` not a simple operand | Span measurement stops at nested array boundary; remaining ops fail admission |

## Lineage

- ADR 0284 (object destructuring) and ADR 0287 (spread calls) provide the
  precedent for the constraint policy: decline computed keys, spread elements,
  name inference, and nested complex patterns.
- `ExpressionOpKind.ArraySpread`, `ObjectSpread`, `DefineObjectMethod*`, and
  `DefineObjectAccessor*` already decline via `ObjectLiteralOrSpreadDependency`
  in `TryFindExpressionDecline` before reaching the admission sites.
- `LoadFunctionLiteral` and `LoadClassLiteral` already decline via
  `ObjectLiteralOrSpreadDependency` before reaching the admission sites.
- `DefineComputedObjectProperty` without `AllowNameInference` passes the per-op
  scan (line 998 in `TryFindExpressionDecline`) but is declined by
  `TryMeasureSimpleObjectLiteralSpan` at the span level.

## Consequences

- `fn([a, b])`, `fn({x: a, y: b})`, `fn([])`, `fn({})`, and mixed-arg calls
  (`fn(a, [b, c])`) now route through the unified bytecode production path.
- `obj.prop === [a, b]` and `obj.prop === {x: a}` are now admitted by the
  binary-expression boundary candidate.
- All previously declining shapes (holey arrays, spread elements, computed keys,
  methods, accessors, name inference, private names) continue to decline.
- No opcode changes, no VM changes — only the admission-gate layer is widened.
