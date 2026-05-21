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

## Why

Issue #1391 audited AST runtime seams before bytecode expansion. The audit found
no direct `EvaluateExpression(` or `ProfileEvaluateExpression(` hits in
`TypedAstEvaluator.ExecutionPlanRunner*`, while broader searches found stale
`StatementInstruction` comments, diagnostic enum values, legacy evaluators,
dynamic-only boundaries, and a profiling bridge. Future bytecode work needs
that classification discipline so stale references do not create new mixed
AST/IR fallback paths and real legacy boundaries remain visible follow-up work.
