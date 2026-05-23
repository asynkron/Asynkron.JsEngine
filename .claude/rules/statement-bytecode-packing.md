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
7. For optional compact payload fields, store explicit presence metadata instead
   of inferring presence from sentinel values after packing into structs. A
   default struct payload can contain zero-valued scope or slot ids that look
   populated, so byte estimates and decoded records must branch on a real
   `Has...` flag for optional metadata.
8. Treat `CompactStatementStorage` as the owner seam for statement compact
   storage, but not as runtime routing by default. When adding a supported
   family, update `CompactStatementInstructionTaxonomy`, the owner reference
   tables, decode parity, diagnostics, and storage accounting together. Keep
   deferred families explicit in `CompactStatementStorageBoundary` instead of
   letting unsupported instructions disappear from estimates.
9. Keep the diagnostics codec shape owner-compatible even while its class name
   remains diagnostic. Encoded statement data should be `CompactStatementHeader`
   plus typed `CompactStatementPayload` references or table ids, not
   diagnostic-only delimiter strings or overloaded catch-all fields. The codec
   may keep compatibility overloads that embed references for direct
   encode/decode tests, but owner-boundary callers should flow through shared
   reference tables and runtime execution must still route through decoded
   `ExecutionInstruction` views until a dedicated runtime-routing slice changes
   that contract.

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

Related ADR: `docs/adrs/0094-compact-statement-bytecode-encoding-design-from-current-ir.md`.
