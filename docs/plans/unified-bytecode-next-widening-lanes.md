# Widen unified bytecode production: operator, literal, and property lanes

## Baseline
- Current main executes `this`-based property reads, property-read+production-binary expressions (`this.x === v`), named/computed/spread call invocation, try/catch/finally, with-backed dynamic names, block lexical scopes, and iterator/for-in/array+object destructuring drivers fully inside `UnifiedBytecodeVirtualMachine` (no mixed execution).
- Admitted binary operators: `+ - * / %`, `==`, `===`, `!==`, `< <= > >=` only. Bitwise, shift, exponent, loose `!=`, `in`, and `instanceof` still decline with `PrototypeOnlyBinaryOpcode`.
- Object/array literal helpers, logical/nullish short-circuit operators, and compound property writes (`this.x += v`) still decline before VM execution.
- Each batch below is an independent lane over disjoint code; lanes can be claimed in parallel.

## Batch 1 - Value binary operator widening
- [ ] Extend `UnifiedBytecodeVirtualMachine.ApplyBinaryOperator` to execute `BitwiseAnd/Or/Xor`, `LeftShift/RightShift/UnsignedRightShift`, `Power`, loose `NotEqual`, `In`, and `InstanceOf`, reusing the existing `JsOps`/`TypedAstEvaluator` value semantics (no new evaluation logic).
- [ ] Widen `UnifiedBytecodeCompiler.IsSupportedBinaryOperator` and `UnifiedBytecodeProductionEligibility.IsProductionBinaryOperator` to the same operator set; keep `FormatBinaryOperator` decline messages exhaustive for anything still unsupported.
- [ ] Add focused eligibility + invocation tests proving each newly admitted operator routes through the production VM and produces correct results (including `in`/`instanceof` TypeError-on-non-object behaviour).

## Batch 2 - Object literal shorthand and name inference
- [ ] Extend the VM `DefineObjectProperty`/`DefineComputedObjectProperty` path (or admit it) so shorthand `{ x }` and name-inferred property values execute without falling back.
- [ ] Remove the `AllowNameInference` decline in `UnifiedBytecodeProductionEligibility.TryFindExpressionDecline` and the matching `UnifiedBytecodeCompiler` guard only for the proven shorthand subset; keep object methods, accessors, and object spread declined under `ObjectLiteralOrSpreadDependency`.
- [ ] Add focused eligibility + invocation tests for shorthand and name-inferred object literals; assert methods/accessors/spread still decline.

## Batch 3 - Array spread in array literals
- [ ] Extend the VM array-build opcodes to flatten `ArraySpread` operands left-to-right via the shared `TypedAstEvaluator.EnumerateSpread` helper already used by spread calls, preserving iteration order and side effects.
- [ ] Widen `UnifiedBytecodeProductionEligibility` and `UnifiedBytecodeCompiler` to admit only array literals whose spread operands are activation-resolved simple values; keep object spread and non-simple spread sources declined.
- [ ] Add focused eligibility + invocation tests for `[...a]`, `[a, ...b, c]`, and mixed hole/spread arrays.

## Batch 4 - Compound property writes (read-then-write boundary)
- [ ] Extend the VM compound-property path to execute `this.x op= value` / `obj.x op= value` using the existing `GetNamedPropertyForCompoundSet` + `Binary` + `SetNamedProperty` opcodes for an activation-resolved base and simple RHS operand.
- [ ] Replace the `PropertyWriteDependency` decline for this exact shape with an admit in `UnifiedBytecodeProductionEligibility`; keep computed compound writes, logical-assignment writes, and deeper chains declined.
- [ ] Add focused eligibility + invocation tests for named compound property writes with each production binary operator; assert unsupported compound shapes still decline.

## Batch 5 - Logical and nullish operators
- [ ] Add VM execution for the short-circuit expression ops (`JumpIfShortCircuited`) that back `&&`, `||`, and `??`, keeping single-program operand-stack semantics and observable left-operand side effects.
- [ ] Widen `UnifiedBytecodeProductionEligibility` to admit only the short-circuit shape that the VM owns; keep optional-chaining (`JumpIfNullish` with `ReplaceWithUndefined`) declined under `OptionalChainDependency`.
- [ ] Add focused eligibility + invocation tests for `a && b`, `a || b`, `a ?? b` with slot/literal/`this`-property operands; assert optional chaining still declines.

## Deferred Lanes (explicitly not in this plan)
- [ ] Optional chaining reads and calls (`a?.b`, `a?.()`, `a?.[k]`).
- [ ] Super property reads/writes/updates (`super.x`).
- [ ] Private field access (`#x` read/write/`in`).
- [ ] Construct and `super()` constructor execution.
- [ ] Object methods, accessors, and object spread literal members.
- [ ] Function/class literal values and `arguments`-object dependencies.
- [ ] `YieldStar` delegated return/throw and async iterator drivers.
- [ ] Multi-driver labeled break/continue cleanup (driver-crossing).

## Evidence / Guardrails
- [ ] Keep all-or-nothing production routing: accepted programs execute fully in `UnifiedBytecodeVirtualMachine` with no fallback into `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluators.
- [ ] Every widening must keep its out-of-scope siblings under an explicit decline family — no silent acceptance of unproven shapes.
- [ ] Update `docs/unified-bytecode-expansion-contract.md` support matrix for each landed lane.
- [ ] Source-of-truth files while implementing:
  - `docs/unified-bytecode-expansion-contract.md`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`

## Proof Pack
- [ ] `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"`
- [ ] `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"`
- [ ] `rtk dotnet build -c Release` (0 errors, 0 warnings)
- [ ] `rtk ./benchmark.sh --smoke` (no routing regression)
