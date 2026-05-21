# ADR 0096: Keep static object key bytecode normalization spec-owned

## Status

Accepted

## Context

Issue #1442 / PR #1453 expanded expression bytecode coverage for static object
literal keys represented as AST nodes. Identifier key nodes were a plain syntax
normalization gap, but review found the first literal-key implementation used
diagnostic formatting for `JsValue` keys.

That was not equivalent to ECMAScript property-key semantics. String literal
keys could leak diagnostic quotes, and BigInt literal keys could leak the
diagnostic `n` suffix. The bytecode compiler had to produce the same property
names as the runtime object literal path while still keeping unsupported static
and computed key shapes classified through the expression-program failure
surface.

## Decision

Keep static object literal key normalization in `ExpressionProgramCompiler`
aligned with JavaScript property-key semantics, not host diagnostic formatting.

When a static object key arrives as an `IdentifierExpression`, normalize it from
the identifier symbol name. When it arrives as a `LiteralExpression`, unwrap the
literal value and normalize `JsValue` keys through `JsOps.ToPropertyName(...)`.
Do not rely on `JsValue.ToString()` or broad `object.ToString()` fallback for
literal-key AST nodes.

Static key support should remain a compile-time normalization path. Computed
keys that are not represented as expression keys should continue to fail with
`InvalidComputedObjectKey`, and unknown static AST key nodes should remain in
the `UnsupportedStaticObjectPropertyName` backlog until a focused shape-specific
proof exists.

## Consequences

- Future object literal bytecode work should test parser-literal keys and
  manually constructed AST key nodes separately.
- Literal-key regression coverage should include string, number, and BigInt
  keys so diagnostic formatting cannot masquerade as property-key conversion.
- Expression-program backlog updates must distinguish "now normalized" key
  shapes from still-unsupported static/computed key shapes instead of treating
  `UnsupportedStaticObjectPropertyName` as one generic bucket.
