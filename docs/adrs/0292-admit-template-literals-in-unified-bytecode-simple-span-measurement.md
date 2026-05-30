# ADR 0292: Admit untagged template literals in unified bytecode, simple span measurement

## Status

Accepted

## Context

Issue #2741 widens production unified-bytecode routing to admit **simple untagged
template literals** as call arguments and as the RHS of named-property write, compound
write, and binary-expression read shapes — e.g., `` fn(`hello ${name}`) ``,
`` obj.prop = `hello ${name}` ``, and `` obj.prop === `done` ``.

Before this slice, `HasSimpleCallArguments` and the property-write/read candidate
helpers only accepted single-op simple operands or array/object literal spans as
value positions. Untagged template literals compile to a multi-op sequence
(``LoadLiteral("") + text-part cycles + substitution-part cycles``) which the
existing helpers rejected, causing the call/property-write gate to decline even
simple logging and formatting patterns.

All required opcodes (`ToString`, `Binary(Add)`, `LoadLiteral`) were already
emitted by `TryCompileTemplateLiteralExpression`, handled by
`UnifiedBytecodeCompiler`, and present in `TryFindPrototypeOnlyOpcode`'s
admitted set. Only the span-level recognition was missing.

## Decision

Introduce one span-measurement helper and update four admission sites:

### `TryMeasureSimpleTemplateLiteralSpan`

Validates the following shape starting at `startIndex`:

1. `LoadLiteral` (seed — the empty-string accumulator emitted first by the compiler)
2. Zero or more cycles, each matching either:
   - **Text part**: `LoadLiteral(string), Binary(Add)`
   - **Substitution part**: `<simple-operand>, ToString, Binary(Add)` where
     `simple-operand` is any of `LoadLiteral`, activation-resolved `LoadIdentifier`,
     `LoadThis`, or `LoadNewTarget`

Returns `spanLength = 1` when the `LoadLiteral` seed is not followed by any
matching continuation (bare literal, or a complex substitution that is not a
simple operand). The call site checks `spanLen > 1` to distinguish a real
template span from a standalone literal.

Returns `false` only when `startIndex` does not point to a `LoadLiteral` op.

### `HasSimpleCallArguments` (template literal branch)

A new `else if (op.Kind == ExpressionOpKind.LoadLiteral)` branch is inserted
before the `IsSimpleOperand` fallback. It tries `TryMeasureSimpleTemplateLiteralSpan`;
if `spanLen > 1`, the whole span is consumed as one logical argument. Otherwise
the `LoadLiteral` is consumed as a standalone literal (same as before). This
admits `` fn(`hello ${name}`) ``.

### `TryIsFirstBoundaryPropertyWriteCandidate` (multi-op named-write RHS)

The previous fixed-count check (`OperationCount == 3` for named writes) is
replaced with a last-op dispatch on `SetNamedProperty`. For single-op RHS
(count == 3), `IsSimpleOperand` is used as before. For multi-op RHS, the helper
tries `TryMeasureSimpleTemplateLiteralSpan` and verifies the span fills the RHS
window exactly. Computed writes (`OperationCount == 4`) are unchanged.

### `TryIsFirstBoundaryNamedCompoundPropertyWriteCandidate` (multi-op compound-write RHS)

The fixed count (`OperationCount != 6`) is relaxed to `OperationCount < 6`. The
structural ops (base, `DuplicateTop`, `GetNamedProperty`, `Binary`, `SetNamedProperty`)
are validated by position from each end. The RHS window `[3 .. OperationCount - 3]`
accepts either a single simple operand or a simple template literal span.

### `TryIsFirstBoundaryPropertyReadBinaryExpressionCandidate` (multi-op binary-expression RHS)

A third branch is added after the existing array and object literal span checks,
using `TryMeasureSimpleTemplateLiteralSpan` with `spanLen > 1` to admit template
literal RHS values.

## Constraints (decline families preserved)

| Shape | Reason declined |
|---|---|
| Complex substitution `` `${a + b}` `` | `a + b` is not a simple operand; span scan terminates at seed, returns span=1 |
| Nested template `` `${ `inner` }` `` | Inner template seed is not a simple operand |
| Dynamic lookup `` `${unknownVar}` `` | `LoadIdentifier` not activation-resolved; not a simple operand |
| Tagged template `` tag`...` `` | `LoadTemplateObject` requires VM/compiler support not in scope; compile step declines |

## Lineage

- ADR 0290 introduced the span-measurement pattern for array and object literals.
  This ADR follows the same pattern for template literals.
- `ExpressionOpKind.ToString` was already admitted by `TryFindExpressionDecline`
  (no declining case) and `TryFindPrototypeOnlyOpcode` (`UnifiedBytecodeOpCode.ToString`
  in the admitted list) before this slice.

## Consequences

- `` fn(`hello ${name}`) ``, `` fn(`static text`) ``, and mixed-substitution
  templates now route through the unified bytecode production path.
- `` obj.prop = `hello ${name}` ``, `` obj.prop += ` ${suffix}` ``, and
  `` obj.prop === `done` `` are admitted by the property-write/read helpers.
- All previously declining shapes (complex substitutions, dynamic lookups,
  tagged templates) continue to decline.
- No opcode changes, no VM changes — only the admission-gate layer is widened.
