# ADR 0305: Admit embedded optional-read operands in control expression programs

## Status

Accepted

## Context

PR #2761 / ADR 0293 admitted `&&`, `||`, and `??` expression programs through
owned peek-semantics jump opcodes. PR #2772 / ADR 0297 admitted ternary
expression programs through owned `JumpIfConditionalFalse`, `Pop`, and `Jump`
lowering. Later optional-member work admitted standalone optional reads and
selected optional chains, but optional reads embedded as operands inside those
already-owned control expression programs still declined with
`OptionalChainDependency`.

The blocked shapes were ordinary control expressions whose optional-read operand
was already expressible by owned VM opcodes:

```js
return a && obj?.value;
return a || obj?.value;
return a ?? obj?.value;
return a && obj?.[key];
return a ? obj?.value : fallback;
```

The production risk was not the control operator itself: the surrounding
short-circuit or ternary expression program was already owned. The risk was
accidentally using that ownership to admit broader optional-chain, computed-key,
private-name, or dynamic-lookup shapes that the VM/compiler proof pack did not
cover.

## Decision

Admit bounded optional named and computed property-read spans when they are
operands inside an already-owned control expression program.

Eligibility first proves that the expression program contains one of the owned
control expression operators:

- `JumpIfFalse`
- `JumpIfTrue`
- `JumpIfNotNullish`
- `JumpIfConditionalFalse`

Only then may the optional-read span bypass the generic
`OptionalChainDependency` decline.

The accepted embedded spans stay narrow:

- Named optional read: activation-resolved base followed by
  `GetNamedProperty(IsOptional:true, ShortCircuitOnNullishTarget:false)` with a
  non-private name.
- Computed optional read: activation-resolved base, bounded nullish jump with
  `ReplaceWithUndefined`, simple computed key, and either the short four-op
  `GetComputedProperty` form or the six-op ordinary-read form that includes
  `RequireObjectCoercible(Depth: 1)` and `ResolvePropertyKey` before the final
  non-short-circuit `GetComputedProperty`.

The selector does not add a new source-syntax exception or a second control-flow
recognizer. It only permits the same optional-read spans that the compiler and
VM already own when they appear inside expression programs whose control
operators are already production-owned.

Unsupported optional-chain forms continue to decline before VM execution:

- unbounded or chained computed optional forms
- private names
- dynamic lookup or non-activation-resolved bases
- complex computed keys
- optional writes, updates, deletes, super, and unowned call shapes

## Consequences

- `&&`, `||`, `??`, and `?:` expression programs can now route through
  production unified bytecode when a bounded optional property/element read is
  one operand.
- `OptionalChainDependency` is narrowed again, not removed. The accepted spans
  must remain tied to owned control expression programs and owned optional-read
  opcodes.
- Future operand widening must prove both sides of the boundary: the owning
  control operator and the embedded operand span. A behavior-only test is not
  sufficient; eligibility tests should assert the route and the owned opcodes
  such as `JumpIfShortCircuitFalse`, `JumpIfShortCircuitTrue`,
  `JumpIfShortCircuitNotNullish`, `JumpIfFalse`,
  `GetNamedPropertyOptional`, `JumpIfNullishReplaceUndefined`, and
  `GetComputedProperty`.

## Evidence

- Delivery PR #2851 merged as commit `8a92c7cd`:
  `Admit embedded optional-read operands in control expression programs`.
- Changed files:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- Focused verification from the delivery stage passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Release --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_LogicalAndExpression_WithOptionalChainOperand_Accepts|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_LogicalOrExpression_WithOptionalChainOperand_Accepts|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_NullishCoalescingExpression_WithOptionalChainOperand_Accepts|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_LogicalAndExpression_WithOptionalComputedChainOperand_Accepts|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_ConditionalExpression_WithOptionalChainBranch_Accepts"`
  - Result: 5 passed, 0 failed.

## Related

- `docs/adrs/0293-admit-logical-and-nullish-expressions-in-unified-bytecode-with-peek-jump-semantics.md`
- `docs/adrs/0296-admit-optional-member-access-in-unified-bytecode-with-null-check-opcodes.md`
- `docs/adrs/0297-admit-conditional-ternary-expression-in-unified-bytecode.md`
- `docs/adrs/0298-admit-multi-hop-optional-named-chains-in-unified-bytecode-jump-based-lowering.md`
- `docs/rules/unified-bytecode-prototypes.md`
