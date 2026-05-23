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

### 6.4 Branch-condition await lowering proof shape

Issue #1491 / PR #1501 refined the control-flow-with-expression-condition
classification for async `if` conditions. A safe awaited branch condition is not
a new expression-bytecode opcode. It is a statement-lowering normalization:
evaluate the awaited condition into a synthetic `SimpleVariableDeclaration`
awaited temp, then let `BranchInstruction.ConditionProgram` read that temp
through ordinary expression bytecode.

Direct `if (await value)` and nested shapes such as
`if ((await value).ready)` should be proven as separate payload contracts. The
direct and nested forms share the same published IR shape, but the lowerer must
not flatten every expression that contains an await. The safety guard has to
match the await rewriter traversal and preserve JavaScript evaluation order:
short-circuit logical/nullish forms are not safe for eager hoisting,
`NewExpression.Constructor` must be checked alongside arguments, and
`ConditionalExpression` branches must stay branch-only unless a future slice
proves an equivalent rewrite for that exact shape.

Future compact statement-bytecode work should therefore encode the existing
branch payload shape as expression-program references plus async-resume metadata
where applicable. It should not add branch-time raw AST evaluation or an
`AwaitExpression` expression opcode to cover condition shapes that belong in a
lowering-time evaluation-order proof.

### 6.5 Issue #1517 classification confirmation snapshot

Issue #1517 rechecked this ADR classification against current repo sources:
`InstructionKind.cs` (enum surface) and `Instructions.cs` (payload ownership
and normalization seams). The encode-now, conditional, and deferred family
boundaries in section 6.1 remain current with no instruction-family re-buckets
required for this issue.

This issue stays evidence-only for statement compact-bytecode planning. It does
not authorize runtime/lowering changes, expression-op changes, new AST fallback
seams, or statement storage implementation.

### 6.6 Diagnostic-only statement storage measurement gate

Issue #1520 / PR #1526 added the first statement-instruction storage diagnostic
surface for this staged migration. The accepted slice intentionally did not add
compact runtime routing or dual instruction storage. It added
`StatementInstructionStorageDiagnostics` and focused tests that collect:

1. total execution-plan and instruction counts;
2. a full `InstructionKind` histogram;
3. supported and unsupported compact-estimate histograms; and
4. estimated encoded bytes only for explicitly supported stable families.

The initial supported diagnostic-estimate set is deliberately narrow:
`Jump`, `SetCompletionValue`, `Break`, `Continue`, `BreakableExit`,
`EndFinally`, `LeaveTry`, and `PopEnvironment`. Every other instruction kind
stays visible in the unsupported histogram instead of being folded into an
optimistic storage estimate.

This measurement gate must remain diagnostic-only until a later implementation
slice introduces an owner boundary for compact statement storage and a parity
decode bridge. Future compact statement-bytecode work should extend this
diagnostic surface before moving a family from unsupported to encoded runtime
storage, so storage estimates, migration readiness, and runtime behavior do not
drift apart.

### 7. Debug/printer/test bridge requirements

Maintain today’s inspectability throughout migration.

- `ExecutionPlanPrinter` must decode compact storage into the same readable
  semantic instruction text (kind, next target, effective operands).
- `ExecutionPlanDiagnostics` must support equivalent dumps for failures.
- Tests that assert printable plan shape keep passing via decoded view.
- A temporary encode-decode parity check should compare decoded compact output
  to legacy record output for the same plan.

### 7.1 Issue #1519 diagnostic parity bridge checkpoint

Issue #1519 / PR #1525 implemented the first diagnostic-only encode/decode
bridge for the encode-now pure control-flow families:
`Jump`, `Break`, `Continue`, `SetCompletionValue`, and `BreakableExit`.

This is intentionally not runtime compact storage yet. The accepted shape is a
small `StatementInstructionDiagnosticsCodec` that encodes only stable scalar
operands, decodes back to the existing `ExecutionInstruction` semantic view, and
proves parity by comparing `ExecutionPlanDiagnostics.FormatInstruction(...)`
output for the legacy and decoded instructions. Future bridge expansions should
keep this decoded-view parity contract until record-backed storage can be
removed with broader diagnostics and profiling proof.

### 7.2 Issue #1518 supported-payload parity checkpoint

Issue #1518 / PR #1527 expanded the diagnostic-only bridge into
expression-program-backed statement families while keeping
`ExecutionPlanRunner` on the existing `ExecutionInstruction` records. The
accepted slice added codec support and storage accounting for representative
stable families such as `EvaluateAndDiscard`, `AwaitAndDiscard`, `Throw`,
`Return`, `AssignmentSlot`, `SimpleVariableDeclaration`, and binding/declaration
forms, while unsupported families still remain explicitly reported.

The review checkpoint tightened the parity contract: supported decode must
reconstruct an equivalent semantic instruction record, not only an equivalent
printer line. The codec therefore has to carry every supported field that can
affect the decoded instruction view, including slot scope/index/flat-slot
metadata, declaration flags such as name inference and script-level status,
await-state metadata, and expression or binding payload references owned by the
existing program types. If a payload cannot be represented without guessing or
pulling descriptor-heavy runtime objects into the diagnostic format, keep that
family unsupported and visible in diagnostics until a later normalization slice
owns the payload table.

This checkpoint does not promote the diagnostic bytecode to runtime storage.
It keeps the migration path staged: diagnostics may decode to the current
semantic records for measurement, drift gates, and tests, but runtime execution
still uses the record-backed `ExecutionPlan` until a later owner-backed compact
storage issue proves full parity and profiling impact.

### 7.3 Issue #1579 owner-backed expression reference checkpoint

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-de2be93552`
/ PR #1579 moved the statement storage diagnostic estimate closer to the ADR's
plan-level expression-reference model without changing runtime storage. The
accepted slice added a shared `StatementDiagnosticsExpressionProgramTable`,
encoded supported expression-bearing statement payloads as expression-program
reference ids, and reported owner-backed storage counters such as fixed encoded
bytes, operand-table entries, expression-reference counts, and the expression
program reference-table count.

This is still diagnostic-only evidence. It proves that repeated statement
payloads can be measured through owner-backed expression references, while
runtime execution remains on `ExecutionInstruction` records and
`ExpressionProgram` remains the owner of expression operation storage.

### 7.4 Issue #1580 statement-storage diagnostics reporting checkpoint

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-a9b9306ab8`
extends the diagnostic-only measurement surface by wiring
`StatementInstructionStorageDiagnostics` into `tools/ProfileRunner` behind
`--statement-instruction-storage`.

Current-worktree evidence (forloop profile):

- Focused proof pack:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~StatementInstructionStorageDiagnosticsTests"`
  - Passed: `7`
  - Failed: `0`
- Statement storage diagnostics:
  `rtk dotnet run --project tools/ProfileRunner/ProfileRunner.csproj -c Release -- forloop --statement-instruction-storage`
  - plans: `2`
  - instructions: `18`
  - supported: `8`
  - unsupported: `10`
  - owner-backed encoded bytes: `128`
  - estimated compact encoded bytes: `184`
  - operand table entries: `8`
  - extra operand table entries: `7`
  - expression references: `primary=5`, `secondary=0`, `table_count=5`
  - symbols/bindings: `symbol_operands=2`, `binding_target_operands=0`
  - unsupported-family reasons:
    - `declaration-and-scope`: `6`
    - `assignment-and-mutation`: `2`
    - `branch-control`: `1`
    - `suspend-and-exception-flow`: `1`
- Allocation snapshot:
  `rtk ./tools/profile forloop --memory`
  - total allocated: `6.99 MB`

Interpretation:

- This slice is **diagnostic-only**. It adds runner visibility for existing
  storage estimates and histograms; it does not route runtime execution through
  compact statement storage.
- Relative to the previous forloop allocation baseline (`7.05 MB` in
  `docs/expression-bytecode-baseline-2026-05-22.md`), the `6.99 MB` sample is
  within profiler sampling variance and should be treated as **neutral** for
  runtime-memory claims.
- The unsupported-family histogram remains the migration driver: scope and
  declaration-heavy families still dominate and should be normalized/encoded in
  later staged slices before any runtime storage flip.

The compatibility overloads on `StatementInstructionDiagnosticsCodec` remain
intentional: tests and existing diagnostic callers that do not pass a shared
reference table still need embedded expression programs to round-trip. Future
statement-bytecode work should keep both contracts explicit: shared-table
encoding for plan-level storage estimates, and compatibility embedding only for
diagnostic bridges that lack the table context.

### 7.5 Issue #1570 compact storage owner boundary checkpoint

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-7c3e056ff9`
/ PR #1570 introduced the first compact statement storage owner seam on
`ExecutionPlan` without changing runtime execution. The accepted slice added
`CompactStatementStorage`, `CompactStatementStorageBoundary`, and
`CompactStatementInstructionTaxonomy` so supported instructions can be encoded
into opcode/operand/reference tables while unsupported instruction families
remain explicitly classified as deferred.

This checkpoint moves beyond diagnostic counters, but it is still not a runtime
storage migration. `ExecutionPlan.Instructions` remains the execution source of
truth, and `CreateCompactStatementStorageBoundary()` is an internal seam for
measurement, decode-parity proof, and incremental owner-backed storage work.

The boundary must keep three contracts together:

1. supported-vs-deferred classification comes from the owner taxonomy, not from
   scattered diagnostic heuristics;
2. expression, symbol, and binding-target payloads are stored in owner reference
   tables outside the opcode stream; and
3. `DecodeSemanticView()` reconstructs the current `ExecutionInstruction`
   records for diagnostics and parity tests.

Future slices may expand supported families or start routing selected reads
through the compact owner only after the same slice updates the owner taxonomy,
reference tables, decode bridge, diagnostics, printer/test coverage, and
profiling evidence. Do not treat the existence of
`CreateCompactStatementStorageBoundary()` as permission to bypass
record-backed runtime execution.

### 7.6 Issue #1595 compact-owner diagnostic/printer route checkpoint

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-322b73d626`
/ PR #1595 routed pure control-flow diagnostic printing through
`CompactStatementStorage.DecodeSemanticView()` by adding a printer path for
`CompactStatementStorageBoundary`. The accepted slice proved `Jump`, `Break`,
and `Continue` formatting parity between the original `ExecutionInstruction`
records and the decoded compact-owner semantic view.

This is a diagnostics and printer migration checkpoint, not a runtime storage
migration. Future statement storage work should move readable diagnostics and
tests toward owner-boundary decode paths first, while keeping
`ExecutionPlanRunner` on the published `ExecutionPlan.Instructions` semantic
view until a dedicated runtime-routing slice changes that contract.
Unsupported and deferred families must remain visible in compact-boundary
metadata instead of being hidden behind a partial decoded semantic view.

### 7.7 Issue #1593 sidecar-boundary coverage checkpoint

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-2385af5d32`
/ PR #1593 added the first plan-owned compact statement storage sidecar for the
pure control-flow route-readiness slice. `ExecutionPlan` now carries an optional
`CompactStatementStorageBoundary` built during plan publication, while
`ExecutionPlan.Instructions` remains the runtime source of truth.

The review repair split two boundary intents that must stay separate:

1. `CreateCompactStatementStorageBoundary()` may return the cached
   pure-control-flow sidecar because that sidecar represents the first
   owner-backed route-readiness slice.
2. `CreateDiagnosticCompactStatementStorageBoundary()` must always rebuild from
   the full instruction list with `DiagnosticCoverage` so diagnostic storage
   estimates still include every codec-supported family, such as `Return`, even
   when the plan has a narrower pure-control-flow sidecar.

Future statement storage work must therefore name the boundary mode it needs.
Route-readiness, printer, and parity work may use the cached sidecar when the
slice intentionally targets a narrower family. Coverage and measurement work
must use the diagnostic boundary from the full semantic instruction list so
diagnostic evidence does not regress as runtime-storage seams become narrower
and more publishable.

### 7.8 Issue #planmanual...9c07ab2dcb diagnostics refresh checkpoint

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-9c07ab2dcb`
refreshes the statement-storage evidence from the current worktree without
changing runtime execution or expanding compact runtime routing.

Current-worktree rerun evidence:

- Focused proof pack:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~StatementInstructionStorageDiagnosticsTests"`
  - Passed: `11`
  - Failed: `0`
- Statement storage diagnostics:
  `rtk dotnet run --project tools/ProfileRunner/ProfileRunner.csproj -c Release -- forloop --statement-instruction-storage`
  - plans: `2`
  - instructions: `18`
  - supported: `8`
  - unsupported: `10`
  - owner-backed encoded bytes: `128`
  - estimated compact encoded bytes: `184`
  - operand table entries: `8`
  - extra operand table entries: `7`
  - expression references: `primary=5`, `secondary=0`, `table_count=5`
  - symbols/bindings: `symbol_operands=2`, `binding_target_operands=0`
  - unsupported-family reasons:
    - `declaration-and-scope`: `6`
    - `assignment-and-mutation`: `2`
    - `branch-control`: `1`
    - `suspend-and-exception-flow`: `1`
- Allocation snapshot:
  `rtk ./tools/profile forloop --memory`
  - total allocated: `7.09 MB`

Current supported instruction kinds in this diagnostic snapshot are:
`Return`, `EvaluateAndDiscard`, `SimpleVariableDeclaration`, and
`BreakableExit`. Current unsupported kinds in the same snapshot are:
`PushEnvironment`, `PopEnvironment`, `IncrementSlot`, `FunctionDeclaration`,
`BreakableEnter`, `Branch`, and `CompoundAssignmentSlot`.

Historical delta note:

- Section 6.6 records the original narrow measurement-gate family list from
  issue #1520 (`Jump`, `SetCompletionValue`, `Break`, `Continue`,
  `BreakableExit`, `EndFinally`, `LeaveTry`, `PopEnvironment`).
- Current support/defer behavior is now owned by
  `CompactStatementInstructionTaxonomy` plus
  `StatementInstructionDiagnosticsCodec`, and therefore the current rerun no
  longer treats `PopEnvironment`, `LeaveTry`, or `EndFinally` as supported in
  this profile snapshot.
- This is expected checkpoint drift from owner-seam refactors, not a runtime
  storage migration.

Guardrail:

- This checkpoint is diagnostic-only evidence. `ExecutionPlanRunner` remains on
  the published `ExecutionPlan.Instructions` semantic view and this issue does
  not authorize runtime compact storage routing, lowering rewrites, or new
  AST/IR fallback seams.

### 7.9 Issue #1605 expression-reference storage checkpoint

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-62870b23dd`
/ PR #1605 moved `EvaluateAndDiscard` and `AwaitAndDiscard` compact statement
storage from embedded per-instruction expression payloads to shared expression
program reference IDs. The accepted slice kept runtime execution record-backed,
but made the compact owner boundary responsible for inserting expression
programs once, reusing stable IDs for repeated programs, encoding those IDs in
statement payloads, and decoding the semantic view through the shared
expression table.

Future expression-backed statement families should follow the same owner-backed
shape: encode statement payloads as expression-program references, keep the
plan-level expression table deduplicated, and prove `DecodeSemanticView()`
round-trips the affected `ExecutionInstruction` records through that table.
Compatibility embedding may remain only for diagnostic codec callers without a
shared table context; compact owner storage should not reintroduce embedded
expression payloads.

### 7.10 Issue #1609 binding-target reference checkpoint

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-d12c4380f1`
/ PR #1609 decided the next `BindingVariableDeclaration` step by adding an
explicit diagnostics reference table for `BindingTargetProgram` payloads. The
accepted slice keeps runtime execution record-backed, but makes compact
statement diagnostics encode binding targets as table IDs and decode the
semantic view through that table, alongside the existing expression-program
table.

This is still not full runtime compact-storage readiness for
`BindingVariableDeclaration`. `BindingTargetProgram` remains a recursive object
graph that can include object and array binding shapes, rest targets, defaults,
computed names, and nested `ExpressionProgram` payloads. A reference table makes
the current diagnostic boundary explicit and measurable; it does not normalize
that graph into scalar operand streams or authorize `ExecutionPlanRunner` to
consume compact statement storage directly.

Future `BindingVariableDeclaration` runtime-storage work must therefore treat
the PR #1609 table as a checkpoint, not as the final representation. The
runtime-ready slice still needs an explicit binding-target operand format,
nested expression reference ownership, semantic decode parity for object/array
destructuring shapes, diagnostics accounting, and profiling evidence before
record-backed instruction storage can be retired for this family.

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
