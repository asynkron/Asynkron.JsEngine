# ADR 0093: Keep AST seam audits classified before bytecode expansion

## Status

Accepted

## Context

Issue #1391 audited the current runtime AST seams before more expression
bytecode expansion or compact statement-bytecode design work. The task was
read-only because the risk was not a single implementation bug; it was planning
future bytecode work from stale source references.

The audit ran the requested seam searches:

1. `rg "EvaluateExpression\(|ProfileEvaluateExpression\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`
2. `rg "StatementInstruction|AST-evaluated|AstPayloadLeak|AstReentry" src/Asynkron.JsEngine`

The first search had no direct runner-file hits. The second search found a mix
of legacy AST-evaluation boundaries, dynamic or profiling-only seams, stale
comments, and obsolete compatibility markers. In particular, `InstructionKind`
no longer has a `Statement` member and no concrete `StatementInstruction` type
was found, so many `StatementInstruction` references were not active runtime
instruction evidence.

The remaining real boundaries were the quarantined legacy expression and
statement evaluators, dynamic-only operand/yield-resume support, and the
profiling bridge that still invokes legacy statement evaluation. The highest
risk was treating those intentional boundaries, stale comments, and enum
compatibility markers as the same class of runtime AST re-entry.

Issue #1405, retried by #1414, added a second failure mode: dynamic JavaScript
entry points were being described as generic AST fallback paths even when
supported execution was already lowered into IR/expression bytecode. The audit
covered direct eval, `with`, Function constructors, generated function bodies,
and modules. It found that direct eval and Function constructors parse dynamic
source and then route through script/function IR with explicit failure if a
required plan is missing. Supported `with` shapes are also lowered, with slot
fast paths intentionally disabled where dynamic object environment lookup owns
the semantics. The remaining boundary to treat as the next migration slice is
module body dispatch, where `JsEngine.ExecuteModuleBody` still iterates
non-import/export statements through a per-statement wrapper instead of an
explicit module-body plan/cache.

## Decision

Before expanding expression bytecode or designing compact statement bytecode,
classify AST-seam evidence by runtime meaning:

1. direct calls from `TypedAstEvaluator.ExecutionPlanRunner*` to
   `EvaluateExpression(` or `ProfileEvaluateExpression(` are active runner
   seams and must be treated as bytecode/IR debt;
2. legacy AST evaluators and dynamic-only operands are quarantined boundaries
   unless the new work proves they are on the normal non-dynamic fast path;
3. comments mentioning removed `StatementInstruction` behavior are cleanup
   candidates, not proof that the instruction still exists;
4. enum values such as `AstPayloadLeak` and `AstReentryDetected` remain
   diagnostic or compatibility markers unless an active call site is found;
5. planning notes and implementation issues must record the exact search
   commands and classification, so later bytecode work can start from the
   baseline instead of repeating broad discovery.
6. dynamic-boundary audits must separate `dynamic-but-lowered` eval/with/
   generated-function paths from true module-body dispatch debt before choosing
   an implementation slice.

For dynamic boundaries, use this finer classification:

1. direct `eval` is dynamic-but-lowered through source parse/analyze and
   `EvaluateProgram(..., ExecutionKind.Eval, ...)`, not a blanket AST runtime;
2. `with` support is IR-backed for supported shapes via `WithEmitter`
   (`EnterWithInstruction`/`LeaveWithInstruction`) with expression bytecode for
   the object expression;
3. `Function` / `AsyncFunction` constructors are dynamic-but-lowered generated
   source paths, not a separate AST interpreter mode;
4. normal/generated sync function bodies should execute the cached
   `ExecutionPlan` runner path or fail explicitly when planning is unsupported;
5. module body per-statement dispatch remains the primary AST-runtime leak /
   unclear contract and is the recommended next migration slice;
6. expression payloads on inspected IR paths are bytecode-backed via
   `ExpressionProgram` and `EvaluateExpressionProgram`.

## Consequences

- Future IR/bytecode agents should run the focused AST-seam scans before
  claiming normal-path AST evaluation remains or has been removed.
- New runtime fallbacks should not be justified by stale `StatementInstruction`
  references. Prefer emitter/lowering normalization and deletion of mixed
  AST/IR seams when semantics allow it.
- Cleanup of stale comments is useful but should be separated from runtime
  bytecode design unless the cleanup blocks a concrete implementation.
- Profiling and legacy dynamic boundaries remain legitimate follow-up targets,
  but they need their own issue with proof that the boundary is observable on a
  hot or non-dynamic path.
- Module body execution should be migrated or documented as a coherent
  module-body plan/cache boundary separately from eval and with work, because
  the latter already rely on dynamic-scope safeguards that are easy to regress.
- Follow-up migration work should target module-body dispatch first; this ADR
  explicitly does not authorize runtime behavior changes for eval/with/generated
  constructor paths that are already dynamic-but-lowered.
- This ADR is caused by issues #1391, #1405, and #1414 and complements the root
  `.claude/rules/expression-bytecode-ast-seams.md` rule.
