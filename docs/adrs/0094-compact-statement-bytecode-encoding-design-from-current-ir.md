# ADR 0094: Compact statement-bytecode encoding design from current IR

## Status

Accepted

## Context

Issue #1404 scopes this work as design only: define how to compact the current
statement IR storage without changing JavaScript semantics or introducing a new
execution model. The current runtime already uses `InstructionKind` as a
stable byte discriminator and `ExecutionInstruction` records as a semantic
view, while expression payloads are owned by `ExpressionProgram`.

The safe migration path is to keep the current IR contract, then replace record
storage with compact opcode and operand tables that can be decoded back to the
same semantic instruction view for runtime, diagnostics, and tests.

## Decision

Adopt a staged encoding/storage migration from `ExecutionInstruction` records to
compact statement bytecode with indexed operand tables.

### 1. Encoding shape

- Keep `InstructionKind` as the canonical opcode id (byte domain).
- Store statement stream as parallel compact arrays:
  - `Opcode[]` (byte per instruction)
  - `Next[]` (packed target index or delta)
  - `OperandRef[]` (index into kind-specific operand tables; `0` for none)
- Preserve `ExecutionInstruction` as a decoded bridge type during migration.
- Preserve `ExecutionPlan` semantics (`EntryPoint`, slot/layout metadata,
  instruction order, jump targets).

### 2. Operand table model

Use kind-family operand tables so hot decode stays small and data-oriented.

- `ControlFlowOperands`: jump/branch targets, optional labels.
- `EnvironmentOperands`: push/pop-env metadata, scope ids, slot maps.
- `DeclarationOperands`: declaration symbol sets and init flags.
- `ExpressionRefOperands`: references to expression payload ids.
- `YieldAwaitOperands`: resume targets, resume symbols, yield/yield* mode.
- `TryFinallyOperands`: handler/finally ranges and unwind metadata.
- `IteratorOperands`: iterator source refs, close/finalization behavior.
- `WithAndDestructureOperands`: with-object refs, destructuring target plans.
- `DescriptorPayloadRefs`: indexes for richer descriptor-like payload objects.

### 3. Mapping from current instruction families

Map current `InstructionKind` families into compact opcode classes.

- Control flow: `Jump`, `Branch`, `Break`, `Continue`, `Return`, `Throw`.
- Environment/scope: environment push/pop, slot/flat-slot access families.
- Declaration and assignment setup: declaration and binding shape instructions.
- Expression-evaluating statements: instructions currently carrying
  `ExpressionProgram` or optional expression payload fields.
- Yield/await/resume: generator and async suspension/resume instructions.
- Try/catch/finally: enter/leave handler blocks and completion routing.
- Iterator and for-in/of: init/move/close and iteration-state instructions.
- With/destructuring helpers: object/binding-target and pattern-support ops.

This mapping is storage-only; runtime meaning, order, and completion behavior
remain the existing IR semantics.

### 4. Expression payload reference model

Compact statement bytecode must reference expression bytecode, not inline it.

- Add a plan-level `ExpressionProgram` table (deduplicated by identity/value).
- Statement operand records hold `expressionProgramId` indexes.
- Constants tables remain owned by each `ExpressionProgram`.
- Existing packed expression op representation remains unchanged.
- No AST fallback is added as part of this migration.

### 5. Stable encode-now families

These families are stable enough to encode first.

- Pure control-flow instructions with scalar operands.
- Scope/environment transitions with settled metadata shape.
- Simple slot/index/symbol operand instructions.
- Instructions already normalized to expression-program references.

### 6. Blockers and defer list

Defer families where payload shape is still moving or object-heavy.

- Instructions with nullable transitional expression fields.
- Descriptor/object payload instructions not yet table-normalized.
- Catch/binding target programs whose representation is still evolving.
- Iterator/source references that still mix structural and semantic payload.
- Any active IR seam currently being normalized in related issues (#1393,
  #1394, #1396).

### 6.1 Concrete `InstructionKind` classification (current repo state)

This table is the build-stage classification source for statement compact
bytecode readiness. It classifies all current `InstructionKind` values from
`InstructionKind.cs` using operand payload shapes in `Instructions.cs`.
Expression payload ownership stays in the expression map documented in
`docs/expression-bytecode-coverage.md`; this ADR only classifies statement-side
storage readiness.

| Family | `InstructionKind` values | Status | Why |
| --- | --- | --- | --- |
| Pure control flow | `Jump`, `Break`, `Continue`, `SetCompletionValue`, `BreakableExit` | Encode now | Scalar index/label metadata only; no semantic migration needed. |
| Control flow with expression condition | `Branch` | Conditional / needs normalization | `ConditionProgram` is already expression-bytecode-backed, but statement compact encoding should reference expression IDs through the shared expression table. |
| Terminal control flow with optional expression payloads | `Throw`, `Return` | Conditional / needs normalization | Both carry optional expression/await payloads (`ThrowProgram`/`ReturnProgram`, `AwaitStateKey`, `AwaitedProgram`) and should use shared expression-reference IDs plus normalized async-resume operand encoding. |
| Direct expression statement | `EvaluateAndDiscard` | Conditional / needs normalization | Stable `ExpressionProgram` payload, but must be encoded as expression reference ID instead of in-record payload. |
| Await expression statement | `AwaitAndDiscard` | Conditional / needs normalization | Uses `AwaitStateKey` + `AwaitedProgram`; expression ref part is stable, but async resume payload needs shared encode pattern. |
| Slot fast paths | `IncrementSlot`, `AssignmentSlot`, `LogicalCompoundAssignmentSlot`, `CompoundAssignmentSlot` | Conditional / needs normalization | Mostly scalar/symbol operands plus optional `ExpressionProgram`/`AwaitedProgram`; encode is safe once optional expression fields are normalized to IDs. |
| Declarations with descriptor payloads | `FunctionDeclaration`, `ClassDeclaration` | Defer | Carry descriptor/program-cache payloads that are object-heavy and should be table-normalized before compact storage migration. |
| Variable declarations | `SimpleVariableDeclaration`, `BindingVariableDeclaration` | Conditional / needs normalization | `SimpleVariableDeclaration` is close (symbol + optional expression), but `BindingVariableDeclaration` carries `BindingTargetProgram` object payload; keep together until binding payload encoding is finalized. |
| Environment transitions | `PushEnvironment`, `PopEnvironment`, `BreakableEnter` | Defer | `PushEnvironment` still carries multiple collection/object payloads (`SlotMap`, lexical/flat-slot metadata), but the transitional `SourceBlock` payload must be retired before the plan is published. Needs operand-table normalization first. |
| Yield/resume | `Yield`, `YieldStar`, `StoreResumeValue` | Defer | Suspension/resume semantics rely on mixed payloads (`AwaitStateKey`, optional awaited/iterable programs, state/result symbols); avoid mixing storage migration with semantic seam work. |
| Try/catch/finally | `EnterTry`, `EnterCatch`, `LeaveTry`, `EndFinally` | Defer | Handler/finally range metadata plus binding/catch-program payloads are still semantically dense; keep deferred until unwind/catch payload normalization is explicit. |
| Iteration drivers (`for..of`) | `IteratorInit`, `IteratorMoveNext`, `IteratorClose` | Defer | Iterator state payload includes TDZ bindings, optional awaited/source references, and iterator driver semantics; still object-heavy. |
| Iteration drivers (`for..in`) | `ForInInit`, `ForInMoveNext` | Defer | Similar to `for..of`: mixed state payloads (`SourceReference`, TDZ metadata, optional await/expression programs) need normalization first. |
| `with` boundaries | `EnterWith`, `LeaveWith` | Defer | `EnterWith` includes dynamic object-source and optional awaited/object program payloads; defer until dynamic-boundary encoding strategy is locked. |
| Array destructuring helpers | `ArrayDestructuringInit`, `ArrayDestructuringElement`, `ArrayDestructuringRest`, `ArrayDestructuringClose` | Defer | Iterator/destructuring state instructions are still an active semantic family with specialized binding behavior; keep out of first storage-only migration wave. |

Guardrail: this classification is storage-planning only. It does not authorize
runtime behavior changes or new AST/IR fallback seams.

### 6.2 Return/throw await payload proof shape

Issue #1485 / PR #1496 refined the terminal control-flow payload classification
for return/throw await forms. Direct `return await value` and
`throw await value` remain on the dedicated instruction `AwaitedProgram` +
`AwaitStateKey` path. Nested await inside a larger terminal expression, such as
`return (await value) + 1` or `throw (await value) + 1`, should be lowered into a
synthetic awaited temp and then emitted as an ordinary bytecode-backed
`ReturnProgram` or `ThrowProgram` that reads that temp.

This is a statement-lowering normalization rule, not an expression-bytecode
runtime expansion. Do not add an `AwaitExpression` expression op or rewrite
direct await into synthetic temps only to match the nested-await shape. Future
compact statement-bytecode work should preserve both payload forms and encode
their async-resume metadata explicitly.

### 6.3 PushEnvironment source-block retirement

Issue #1490 / PR #1499 refined the environment-transition payload
classification without moving `PushEnvironment` into the encode-now set.
`PushEnvironmentInstruction.SourceBlock` is an analysis-only AST payload: slot
assignment and identifier collection may read it before publication, but the
returned `ExecutionPlan.Instructions` must not retain it.

Keep the retirement in the plan-lowering hook after slot analysis, not as an
ad hoc final publication scrub and not at emission time. The invariant belongs
near `LowerExpressionPayloads()` / `ValidateLoweredPayloads()` so future
statement-payload work has one enforcement point for AST payload retirement.
Flat-slot mapping should remain a separate pass from payload lowering.

This does not make environment transitions compact-encoding ready by itself.
`PushEnvironment` still needs operand-table normalization for slot maps,
lexical names, and flat-slot metadata before it can leave the deferred family.

### 7. Debug/printer/test bridge requirements

Maintain today’s inspectability throughout migration.

- `ExecutionPlanPrinter` must decode compact storage into the same readable
  semantic instruction text (kind, next target, effective operands).
- `ExecutionPlanDiagnostics` must support equivalent dumps for failures.
- Tests that assert printable plan shape keep passing via decoded view.
- A temporary encode-decode parity check should compare decoded compact output
  to legacy record output for the same plan.

### 8. Staged migration

1. Introduce compact storage schema in `ExecutionPlan` behind an internal
   feature seam while keeping record-backed runtime path active.
2. Implement decode bridge to materialize semantic instruction views for
   runner/printer/diagnostics.
3. Migrate stable instruction families first and validate parity.
4. Migrate deferred families after payload normalization in their owning issues.
5. Remove record-backed storage only after parity, diagnostics, and profiling
   checks remain green.

## Consequences

- The project can reduce statement IR memory footprint without semantic churn.
- Work stays aligned with the existing IR-first, AST-seam-reduction strategy.
- Implementation risk is split into low-risk encode-now families and explicit
  blockers tied to related normalization issues.
- Debuggability is preserved because compact storage must always be decodable to
  the current semantic instruction view.
