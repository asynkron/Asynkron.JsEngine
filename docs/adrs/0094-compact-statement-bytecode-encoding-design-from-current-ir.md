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
