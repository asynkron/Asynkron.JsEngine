# Expression Bytecode AST Seam Classification

When changing expression bytecode, statement lowering, or IR execution to
remove AST evaluation, classify each AST-seam hit before designing a new
fallback or cleanup.

## Rules

1. Start from the focused runner seam scan:
   `rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`.
   Treat direct hits there as active runtime debt.
2. Search historical seam markers with
   `rg "StatementInstruction|AST-evaluated|AstPayloadLeak|AstReentry" src/Asynkron.JsEngine`,
   but classify each hit before acting on it.
3. Do not infer that `StatementInstruction` exists from comments. Confirm a
   concrete instruction type or an `InstructionKind` member before treating it
   as active runtime behavior.
4. Treat `AstPayloadLeak` and `AstReentryDetected` as diagnostic or
   compatibility markers unless a current call site proves they are reachable.
5. Keep legacy AST evaluators, dynamic operand evaluation, and profiling bridges
   separate in reports and implementation plans. A dynamic-only or profiling
   boundary does not justify adding a normal-path AST fallback.
6. If a suspending or nested shape still needs runtime AST evaluation, prefer
   emit-time or lowering-time normalization into existing bytecode/IR
   instructions when JavaScript evaluation order can be proven.
7. Treat runner-file `CS0618` pragmas and old "legacy AST fallback" wording as
   compatibility/comment evidence unless the focused runner scan or a concrete
   call site proves direct AST evaluation. Outer execution fallback ownership
   belongs to planning/lowering diagnostics, not to the runner compatibility
   overload comments.
8. For dynamic JavaScript boundary audits, classify direct eval, `with`,
   Function/AsyncFunction constructors, generated function bodies, and modules
   separately before picking a migration target. Do not group
   `dynamic-but-lowered` eval/with/generated-function paths with the remaining
   module-body dispatch leak unless a current call site proves the same AST
   runtime behavior.
9. When turning a source seam scan into an automated test, assert that the
   expected source files were discovered before asserting zero forbidden calls.
   A source gate that can pass with zero scanned files is not a guardrail.
10. When reporting or planning `UnsupportedExpressionProgram` backlog work,
   derive the bucket list from the current compiler/diagnostic surfaces and
   explicitly rank catch-all buckets such as `UnsupportedExpressionNode`.
   Treat catch-all buckets as high-risk/deferred until narrower typed buckets
   have been burned down, so one implementation slice does not mix unrelated
   semantic risks.

## Dynamic Boundary Classification (#1405 Retry)

Use this boundary map when documenting or planning expression-bytecode vs AST
work. Do not collapse these seams into one generic "AST fallback" bucket.

1. Direct `eval`: dynamic-but-lowered. `EvalHostFunction` parses source, builds
   eval environments, and executes through
   `EvaluateProgram(..., ExecutionKind.Eval, ...)`.
2. `with` statements: IR-only for supported shapes. `WithEmitter` lowers the
   object expression into expression bytecode and emits
   `EnterWithInstruction`/`LeaveWithInstruction`; dynamic lookup is handled by
   runtime scope behavior, not AST expression walking.
3. `Function` / `AsyncFunction` constructors: dynamic-but-lowered. Generated
   source is parsed and executed through the normal program/function execution
   pipeline.
4. Normal/generated sync function bodies: IR runner path. Supported functions
   execute cached `ExecutionPlan` via `ExecutionPlanRunner`; expected plan
   failures throw instead of silently falling back to AST evaluation.
5. Module body dispatch: AST-runtime leak / unclear migration target.
   Non-import/export module statements still run through per-statement wrapper
   execution in `JsEngine` and should be treated as the next migration slice.
6. Expression payloads on inspected IR paths: bytecode-backed via
   `ExpressionProgram` and `EvaluateExpressionProgram`, not raw AST expression
   walking.

## Why

Issue #1391 audited AST runtime seams before bytecode expansion. The audit found
no direct `EvaluateExpression(` or `ProfileEvaluateExpression(` hits in
`TypedAstEvaluator.ExecutionPlanRunner*`, while broader searches found stale
`StatementInstruction` comments, diagnostic enum values, legacy evaluators,
dynamic-only boundaries, and a profiling bridge. Future bytecode work needs
that classification discipline so stale references do not create new mixed
AST/IR fallback paths and real legacy boundaries remain visible follow-up work.

Issue #1405, retried by #1414, applied the same lesson to dynamic boundaries.
Direct eval and Function constructors parse dynamic source and then lower
through script or function IR; supported `with` execution is already lowered
while deliberately using dynamic environment lookup instead of user slot fast
paths. The durable next slice is module body execution, where non-import/export
statements still pass through a per-statement wrapper rather than an explicit
module-body plan/cache. Future agents need this split so eval/with work does not
absorb the module-body migration or accidentally remove dynamic-scope
safeguards.

Issue #1408 added execution-plan diagnostics drift gates. Review found the
runner seam source-gate test could pass vacuously if no
`TypedAstEvaluator.ExecutionPlanRunner*.cs` files were found, so source-scan
tests must prove discovery before they claim absence of forbidden AST seams.

Issue #1435 reran the focused runner seam scan and confirmed the direct runner
call surface was still clean. The durable lesson was not a runtime change: stale
runner comments and `CS0618` rationale can make compatibility/resume overloads
look like active AST fallbacks, while actual outer fallback classification lives
in `ExecutionPlanBuilder`, emitter diagnostics, and
`ExpressionProgramCompileFailure` / `SetExpressionProgramFailure(...)`.

Issue #1436 / PR #1443 added the first durable
`UnsupportedExpressionProgram` backlog report for bytecode expansion. Review
found that the initial report ranked the narrower buckets but did not explicitly
rank the broad `UnsupportedExpressionNode` catch-all, which could have made a
future agent pick a mixed-risk implementation slice. Future backlog reports
must classify and defer catch-all buckets until narrower diagnostics make the
remaining work specific.
