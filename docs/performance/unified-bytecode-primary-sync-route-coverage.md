# Unified Bytecode Primary Sync Route Coverage

Date: 2026-05-29
Updated: 2026-06-02
Issues: #2634 (primary route boundary recording),
`planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-3df21bc582`
(break/continue decline taxonomy cleanup)
Based on: ADR 0278 (Keep unified-bytecode ordinary sync primary route source-gated)

## Coverage Summary

For ordinary sync functions that pass both `CanUseProductionUnifiedBytecodeFastPath` and `UnifiedBytecodeProductionEligibility.Evaluate`, the unified-bytecode production VM is the primary default attempt before simple IR shortcuts and fallback routes.

### Workload: propertyaccess

**Workload shape**: Repeated direct and nested property access in arithmetic loops.

**Profile commands**:
```bash
rtk ./benchmark.sh propertyaccess
rtk ./tools/profile propertyaccess --cpu
```

**Production boundary**: Direct and computed property operations with accepted opcodes:
- `LoadSlot`, `LoadLiteral`, `StoreSlot`, `Binary`
- `GetNamedProperty`, `GetComputedProperty`
- `SetNamedProperty`, `SetComputedProperty`
- `UpdateNamedProperty`, `UpdateComputedProperty`

**Route coverage**: 100% of accepted propertyaccess functions attempt `UnifiedBytecodeVirtualMachine` before `SyncIrCallTrampoline` or generic `ExecutionPlanRunner` interpretation.

- Unified-bytecode-production-fast-path hit count: All expressions within accepted boundary
- Fallback route hit count (SyncIrCallTrampoline/simple-ir): 0 for accepted boundary
- Hit-rate: 100% for accepted expressions

**Decline bucket summary**: Pre-VM declines include:
- Logical assignment (`&&=`, `||=`, `??=`)
- Optional chaining, `super`, `delete`
- Call/construct shapes
- Dynamic activation, `arguments`, `this`, `new.target`

Evidence: See `docs/performance/propertyaccess-unified-bytecode-production-profile-evidence.md`.

### Workload: forloop

**Workload shape**: Arithmetic operations in loop control flow.

**Profile commands**:
```bash
rtk ./benchmark.sh forloop
rtk ./tools/profile forloop --memory
```

**Production boundary**: Direct slot loads, literals, binary operations, and canonical loop control:
- `LoadSlot`, `LoadLiteral`, `StoreSlot`
- `Binary` (core subset: `+`, `-`, `*`, `/`, `%`)
- `Jump` and `JumpIfFalse` (compiler-owned canonical shapes only)

**Route coverage**: 100% of accepted forloop functions attempt `UnifiedBytecodeVirtualMachine` before generic fallback routes.

- Unified-bytecode-production-fast-path hit count: All expressions within accepted boundary
- Fallback route hit count (SyncIrCallTrampoline/simple-ir): 0 for accepted boundary
- Hit-rate: 100% for accepted expressions

**Decline bucket summary**: Pre-VM declines include:
- Async/generator functions
- Noncanonical loops, nested branches
- Captured or dynamic activation
- Call/construct shapes
- Unsupported binary operators

Evidence: See `docs/performance/unified-bytecode-branch-production-routing.md`.

## Claim Scope

This document records route coverage for the accepted production boundary only.
Coverage is 100% for expressions that pass production-eligibility gates.
Unsupported expressions decline before VM execution and are not included in hit-rate calculation.
Simple literal returns, simple parameter returns, and simple parameter binary or
binary-chain returns are included in the accepted production boundary when their
plans are otherwise production-bytecode eligible; their legacy simple-IR
shortcuts now run only as fallbacks for non-eligible shapes.
Break/continue-specific loop control is no longer an active decline bucket; admitted
unlabeled and resolved labeled control flow routes through the compiler-owned jump
target model. Unsupported driver topology should decline through the concrete
driver-state or plan-shape gate that matches the failing shape.

## Proof Commands

**Eligibility tests**:
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"
```

**Invocation tests**:
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"
```

**No AST-eval seam regression**:
```bash
rtk rg "EvaluateExpression\(|ProfileEvaluateExpression\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
```

Result: No fallback from accepted expressions to AST evaluation.
