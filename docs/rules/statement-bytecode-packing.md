# Statement Bytecode Packing

When changing compact statement-instruction storage, keep the work
measurement-led and separate from runtime routing until the encoded owner and
decode bridge are explicit.

## Rules

1. Start compact statement-bytecode work from
   `StatementInstructionStorageDiagnostics` or an intentionally updated
   successor diagnostic. Capture plan count, instruction count, full
   `InstructionKind` histogram, supported histogram, unsupported histogram, and
   estimated encoded bytes before claiming storage impact.
2. Keep unsupported instruction families visible. Do not fold conditional or
   deferred families into encoded-byte estimates until their operand payloads
   have an explicit compact representation and proof.
3. Treat diagnostic estimates as planning evidence only. They do not authorize
   runtime compact routing, dual instruction storage, or record-backed storage
   removal.
4. When moving a statement family from diagnostic estimate to runtime compact
   storage, update the statement storage owner, decode bridge, printer,
   diagnostics, focused parity tests, and storage accounting in the same slice.
5. Keep expression payloads referenced through `ExpressionProgram` owner APIs.
   Statement bytecode should reference expression-program IDs or owner-backed
   handles, not inline expression operations or depend on expression backing
   arrays. For diagnostic storage estimates, count shared expression-program
   reference-table entries separately from statement fixed-header and operand
   table bytes.
6. When expanding diagnostic encode/decode support, prove decoded
   `ExecutionInstruction` record equivalence for every supported payload field,
   not only printer-equivalent output. Slot metadata, flat-slot ids,
   declaration flags, await state keys, and expression/binding programs that
   affect the semantic view must round-trip or the family must stay unsupported.
   If the shared plan-level expression table is unavailable, compatibility
   overloads must keep embedded expression programs so existing diagnostic
   encode/decode callers do not silently decode empty payloads.
7. When proving expression-program reference-table storage for statement
   families with multiple payload slots, use distinct non-empty
   `ExpressionProgram` instances for each semantic slot and assert the expected
   table entry by index. Reusing `ExpressionProgram.Empty` for every slot proves
   only that "some empty payload" survived; it does not catch swapped value vs
   awaited references, initializer vs awaited references, or off-by-one table
   IDs.
8. For optional compact payload fields, store explicit presence metadata instead
   of inferring presence from sentinel values after packing into structs. A
   default struct payload can contain zero-valued scope or slot ids that look
   populated, so byte estimates and decoded records must branch on a real
   `Has...` flag for optional metadata.
9. Treat `CompactStatementStorage` as the owner seam for statement compact
   storage, but not as runtime routing by default. When adding a supported
   family, update `CompactStatementInstructionTaxonomy`, the owner reference
   tables, decode parity, diagnostics, and storage accounting together. Keep
   deferred families explicit in `CompactStatementStorageBoundary` instead of
   letting unsupported instructions disappear from estimates.
10. Keep the diagnostics codec shape owner-compatible even while its class name
   remains diagnostic. Encoded statement data should be `CompactStatementHeader`
   plus typed `CompactStatementPayload` references or table ids, not
   diagnostic-only delimiter strings or overloaded catch-all fields. The codec
   may keep compatibility overloads that embed references for direct
   encode/decode tests, but owner-boundary callers should flow through shared
   reference tables and runtime execution must still route through decoded
   `ExecutionInstruction` views until a dedicated runtime-routing slice changes
   that contract.
11. Route diagnostic and printer consumers through the compact owner boundary
    when a supported statement family is ready for owner-backed semantic views.
    Decode via `CompactStatementStorage.DecodeSemanticView()` and prove parity
    against the original `ExecutionInstruction` records. Do not treat a printer
    or diagnostics bridge as permission to change `ExecutionPlanRunner` or to
    hide unsupported/deferred families behind a partial decoded view.
12. When parity coverage claims a supported `InstructionKind`, make the test
    exercise that kind's comparison branch. If representative source scripts do
    not lower to a direct instance of the kind, add a minimal synthetic
    `ExecutionPlan` probe for that kind instead of relying only on membership in
    an expected-kind set.
13. For forloop statement-storage measurement, use the ProfileRunner
    `--statement-instruction-storage` flag to capture storage diagnostics before
    comparing profiler allocation samples. Treat small `./tools/profile forloop
    --memory` differences as sampling context unless the same slice changes
    runtime storage and proves a real allocation delta.
14. Keep route-readiness sidecars separate from diagnostic coverage boundaries.
    A cached `ExecutionPlan` compact sidecar may intentionally cover only the
    family being prepared for runtime routing, such as pure control flow.
    Storage diagnostics must request an explicit diagnostic boundary rebuilt
    from the full semantic instruction list so codec-supported families outside
    the sidecar still appear in coverage and byte estimates.
15. Treat historical ADR-supported family lists as snapshots, not current
    support contracts. Before calling a supported-vs-unsupported delta a
    regression, re-check the current `CompactStatementInstructionTaxonomy`,
    `StatementInstructionDiagnosticsCodec`, and the live
    `--statement-instruction-storage` output. Owner-seam refactors can move
    kinds between supported and unsupported diagnostic buckets without changing
    runtime execution.
16. Treat `BindingTargetProgram` reference tables as a diagnostic checkpoint,
    not as proof that `BindingVariableDeclaration` is runtime compact-storage
    ready. A binding-target table may preserve the current semantic object graph
    for diagnostics and parity tests, but runtime storage work still needs an
    explicit normalized operand format for recursive object/array/rest/default
    shapes and their nested expression-program references.
17. Split broad deferred instruction families when one member has a proven
    scalar or symbol-only compact payload. For yield/resume work,
    `StoreResumeValue` can be supported through the existing header plus symbol
    reference table, while `Yield` and `YieldStar` must remain deferred until
    their suspension, awaited, iterable, and state payloads have explicit compact
    representations and parity tests.
18. Descriptor-backed declaration instructions can leave the deferred bucket
    only when the descriptor object itself is table-referenced by the compact
    owner. For `FunctionDeclaration` and `ClassDeclaration`, encode stable
    descriptor reference IDs, keep separate function/class descriptor tables,
    include those table counts in storage diagnostics, and prove semantic
    decode parity with descriptor-backed synthetic instructions. Do not inline
    descriptor objects into compact payloads or count declaration support from
    source-plan histograms alone.
19. Environment-transition families may be narrowed one member at a time only
    after the supported member has an explicit payload shape and record-level
    parity proof. For `PushEnvironment`, treat
    `CompactPushEnvironmentPayload` and the compact owner reference table as a
    diagnostic checkpoint for scope/slot metadata. For `PopEnvironment`, encode
    only the scalar companion payload (`ScopeId`, `AllowPooling`, and `Next`)
    and prove semantic decode parity before moving it out of the deferred
    bucket. Neither checkpoint is runtime compact-storage readiness for
    `BreakableEnter` or environment transitions as a whole. Keep `SourceBlock`
    as an analysis-only payload that must be retired before published plans,
    and keep `ExecutionPlanRunner` record-backed until a separate
    runtime-routing slice proves the full environment operand model.
20. Keep broad runtime routing and record-backed instruction removal behind
    deferred-payload normalization while the live statement-storage profile
    still reports unsupported families. A migration report or diagnostic count
    can recommend the next slice, but it should not become a runtime flip until
    the unsupported-family histogram, decode parity tests, runner AST seam scan,
    and forloop memory profile all support that specific routing change.

## Why

Issue #1520 / PR #1526 added the first statement-instruction storage diagnostic
surface as a deliberate migration point after ADR 0094 defined compact
statement-bytecode design. The safe slice collected counts, histograms,
supported-vs-unsupported separation, and narrow encoded-byte estimates for
stable families only. It explicitly avoided compact runtime interpreter routing.
Future agents need this guardrail so statement-bytecode packing does not skip
the measurement gate, hide unsupported families inside optimistic estimates, or
mix storage-format work with semantic runner changes.

Issue #1518 / PR #1527 expanded the diagnostic codec from scalar control-flow
families into expression-program-backed statement families. Review found that
printer-equivalent output was too weak for AC-2 because it could miss supported
record payloads such as `AssignmentSlotInstruction` scope/slot metadata and
`SimpleVariableDeclarationInstruction` flags. Future bridge expansions need
record-level parity tests for supported families so the decoded diagnostic view
remains a trustworthy stand-in for the current semantic instruction records.

Issue #1562 / PR #1563 fixed a red-main regression in
`StatementInstructionDiagnosticsCodec` where `EncodedStatementSidePayload` became
a struct and its default zero-valued `ScopeId`/`FlatSlotId` made no-payload
control-flow instructions look like they carried assignment metadata. The simple
family compact estimate jumped from 64 to 96 bytes until the codec added an
explicit assignment-metadata presence flag. Future optional payload fields need
the same explicit presence bit so diagnostic storage estimates remain stable and
do not confuse default values with meaningful metadata.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-de2be93552`
/ PR #1579 extended `StatementInstructionStorageDiagnostics` with owner-backed
encoded-byte counters and a shared expression-program reference table. The
lesson is that statement storage estimates should measure expression payloads
as references owned by a plan-level table, not as duplicated embedded programs
or decoded expression operation arrays. The same slice also had to keep
compatibility overloads embedding expression programs for callers without the
shared table; without that bridge, diagnostic round-trips can look structurally
encoded while decoding semantically empty expression payloads.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-7c3e056ff9`
/ PR #1570 introduced `CompactStatementStorage` and
`ExecutionPlan.CreateCompactStatementStorageBoundary()` as the first compact
statement storage owner boundary. The lesson is that the owner seam centralizes
taxonomy, opcode/operand/reference tables, and semantic decode parity, but it
still leaves `ExecutionPlan.Instructions` as the runtime source of truth.
Future work must not treat the boundary as a hidden runtime migration, and must
preserve deferred-family visibility so storage estimates and migration scope
remain honest.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-7db25b1c7e`
/ PR #1566 replaced the diagnostics-only statement encoding record with an
owner-compatible compact header/payload shape while preserving decoded
`ExecutionInstruction` runtime execution. The lesson is that diagnostics can
lead the migration only if their encoded data already has the shape a storage
owner can persist: fixed scalar header fields, typed side payload references,
shared expression-program ids for owner-boundary callers, and direct
encode/decode compatibility only as a bridge for tests and diagnostics.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-322b73d626`
/ PR #1595 routed pure control-flow diagnostic printing through the compact
owner decode bridge. The lesson is that diagnostics and printers should migrate
toward the owner boundary before runtime routing, with focused parity tests for
decoded semantic output and an explicit guard that `ExecutionPlanRunner` still
uses the published instruction records until a separate runtime-routing slice.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-40259a2274`
/ PR #1596 added script-plan compact parity coverage for control-flow-heavy
plans, then review found that `Jump` could be listed as an expected supported
family without the script corpus actually exercising the `JumpInstruction`
comparison path. The fix added a minimal synthetic `ExecutionPlan` with a direct
`JumpInstruction` so the same decode-parity assertion covers the claimed kind.
Future parity expansion should prove both family presence and branch execution,
especially for IR kinds whose source-level lowering is context-dependent.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-a9b9306ab8`
/ PR #1594 wired `StatementInstructionStorageDiagnostics` into ProfileRunner via
`--statement-instruction-storage` and recorded the forloop memory sample as
neutral, not an optimization win. Future compact statement-storage work should
keep storage diagnostics and runtime allocation profiling as separate evidence
channels so a tooling-only diagnostic slice does not overclaim CPU or memory
impact.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-2385af5d32`
/ PR #1593 introduced a plan-owned pure-control-flow sidecar, then review caught
that diagnostics had started reusing that narrower cached boundary. The repair
added an explicit diagnostic-coverage boundary so `Return` and other
codec-supported non-sidecar families remain counted. Future agents need this
boundary split because narrowing a publishable runtime-storage sidecar must not
silently narrow the measurement surface that guides later migration slices.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-62870b23dd`
/ PR #1605 moved `EvaluateAndDiscard` and `AwaitAndDiscard` compact statement
storage to shared expression-program reference IDs instead of embedded
per-instruction expression payloads. The lesson is that owner-backed compact
storage must deduplicate expression programs in the plan-level table, encode
stable IDs in statement payloads, and decode semantic views through that table.
Compatibility embedding belongs only in direct diagnostic codec bridges that do
not have shared-table context.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-29225cef6f`
/ PR #1604 added compact storage boundary coverage for `AssignmentSlot` and
`SimpleVariableDeclaration`, then review found the first proof reused
`ExpressionProgram.Empty` for all four expression payload slots. The repair used
four distinct `ExpressionProgram` instances and asserted
`ReferenceTables.ExpressionPrograms[0..3]` by semantic slot. The durable lesson
is that owner-backed reference-table tests must fail when value/awaited or
initializer/awaited references are swapped; repeated empty payloads can make
mis-indexed storage look correct.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-9c07ab2dcb`
/ PR #1607 refreshed the forloop statement-storage diagnostics and found that
the current supported snapshot no longer matched ADR 0094 section 6.6's original
narrow measurement-gate family list: `PopEnvironment`, `LeaveTry`, and
`EndFinally` were no longer supported in that rerun. The lesson is that section
6.6 is historical evidence, while current support/defer behavior is owned by
the taxonomy and diagnostics codec. Future agents should record this as
checkpoint drift from owner-seam refactors unless the current diagnostic proof
or runtime path shows a real behavior change.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-d12c4380f1`
/ PR #1609 added an explicit diagnostics table for `BindingTargetProgram`
payloads used by `BindingVariableDeclaration`. The lesson is that this table is
the correct diagnostic checkpoint because it removes hidden in-payload object
ownership from the compact statement record, but it is not the same as
runtime-ready compact storage. Future agents must not claim
`BindingVariableDeclaration` runtime readiness until the recursive binding
target graph has a normalized operand representation and parity coverage for
object/array destructuring, rest elements, defaults, computed names, and nested
expression-program references.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-76034c375c`
/ PR #1617 promoted `StoreResumeValueInstruction` as the first yield/resume
family member in compact statement diagnostic storage. The lesson is that a
broad deferred family can be narrowed when an individual member has a simple
payload contract: `StoreResumeValue` only needs `Next` and an optional target
symbol reference, while `Yield` and `YieldStar` still carry suspension payloads
that need separate normalization before they leave the deferred set.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-d3f8fd2dad`
/ PR #1618 promoted `FunctionDeclaration` and `ClassDeclaration` diagnostic
storage by adding owner-backed descriptor reference tables and focused manual
descriptor tests. Review-requested AC-6 coverage showed that declaration
support must be proven with real descriptors, explicit payload reference IDs,
snapshot reference-table accounting, supported-kind histogram coverage, and
semantic-view roundtrip assertions. Future declaration-family slices should
follow that pattern instead of treating descriptor-heavy declaration records as
supported because a source script happened to lower into those kinds.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-0a20470768`
/ PR #1616 promoted `PushEnvironment` out of the deferred diagnostics bucket by
adding `EnvironmentTransitionNormalized` classification plus
`CompactPushEnvironmentPayload` reference-table encode/decode support. The
lesson is that collection-heavy scope metadata can become a measured diagnostic
payload only when the owner boundary explicitly carries every semantic field
and round-trips the `PushEnvironmentInstruction` record. This remains a
checkpoint, not a runtime migration: `SourceBlock` retirement stays governed by
the plan-publication invariant, `BreakableEnter` remains deferred, and the
runner continues to execute the record-backed semantic view.

Issue #2050 / PR #2059 promoted `PopEnvironment` as the scalar companion member
of the environment-transition diagnostic family. The lesson is that a
previously deferred family member can be supported without normalizing the
whole family when its payload is fully represented by `ScopeId`, `AllowPooling`,
and `Next`, the codec round-trips those fields, and the unsupported-family
histogram visibly drops. This remains a diagnostics checkpoint only:
`BreakableEnter` and broad runtime environment-transition storage still need
their own operand model and proof before `ExecutionPlanRunner` stops consuming
record-backed `ExecutionInstruction` views.

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-cfc3b74783`
/ PR #1625 produced the final 2026-05-23 migration decision report for the
current statement-bytecode plan. The refreshed forloop diagnostics reported
`supported=12` and `unsupported=6`, with unsupported assignment/mutation,
declaration/scope, branch/control, and suspend/exception-flow families still in
the hot path. The durable lesson is that another deferred-payload normalization
wave is safer than broad runtime compact routing or record-backed storage
removal. Use diagnostic support counts as readiness evidence, not as permission
to switch `ExecutionPlanRunner` off the record-backed instruction view.

## Plan Consolidation Note

Parent follow-on plan: `planmanual1779454308935867000` ("push bytecode from
diagnostics toward runtime storage").

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-b232307d99`
closed stale standalone bytecode planning tasks that were already covered by
completed plan children or by the parent follow-on plan. The lesson is that
statement-bytecode packing now has a plan-owned migration track; new planning
work should be created as children of that parent or under the exact owning
child slice, not as parallel standalone bytecode planning issues for the same
owner surface.

When an older standalone planning issue overlaps a checkpoint documented in
this rule, mark it superseded by the parent follow-on plan or by the exact plan
child that already owns the slice. Avoid opening new duplicate tracker issues
for the same migration step.

Related ADR: `docs/adrs/0094-compact-statement-bytecode-encoding-design-from-current-ir.md`.
Related ADR: `docs/adrs/0098-keep-statement-runtime-routing-behind-deferred-payload-normalization.md`.
