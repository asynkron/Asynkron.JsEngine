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
   arrays.

## Why

Issue #1520 / PR #1526 added the first statement-instruction storage diagnostic
surface as a deliberate migration point after ADR 0094 defined compact
statement-bytecode design. The safe slice collected counts, histograms,
supported-vs-unsupported separation, and narrow encoded-byte estimates for
stable families only. It explicitly avoided compact runtime interpreter routing.
Future agents need this guardrail so statement-bytecode packing does not skip
the measurement gate, hide unsupported families inside optimistic estimates, or
mix storage-format work with semantic runner changes.

Related ADR: `docs/adrs/0094-compact-statement-bytecode-encoding-design-from-current-ir.md`.
