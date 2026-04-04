# Agent Guidelines for Asynkron.JsEngine

This page indexes the agent playbooks. MUST READ AND UNDERSTAND ALL OF THESE before working.

## Standards & Architecture
- [Coding standards and InvariantCulture rules](agents/how-to-coding-standards.md)
- [Architecture overview](agents/how-to-architecture.md)

## Build, Tests, and Profiling
- [Build/test commands and demos](agents/how-to-build-and-test.md)
- [Profiling (scripts, manual traces, hotspots)](agents/how-to-profiling.md)

## Engineering Rules & Workflow
- [Development rules (thread safety, compliance, timeouts)](agents/how-to-development-rules.md)
- [Workflow and GitHub issue logging](agents/how-to-workflow-and-issues.md) — GitHub issues are the persistent working memory; use the gh CLI patterns there to view/create/comment/patch and log every session’s progress.
- [Git worktree workflow](agents/how-to-worktrees.md)

## JsValue and Performance Patterns
- [JsValue usage and evaluator overload pattern](agents/how-to-jsvalue-usage.md)
- [Comparing to Jint (do/don't language)](agents/how-to-compare-jint.md)

## Debugging & Test Strategies
- [Debugging aids (logger assertions, slot metadata)](agents/how-to-debugging.md)
- [Test Bomb methodology](agents/how-to-test-bombs.md)
- [Layered Tests methodology](agents/how-to-layered-tests.md)

## IR / Bytecode Optimization Notes
- Prefer the non-dynamic fast path to be: AST parse/analyze -> IR emit only -> expression payloads as bytecode only. Keep AST evaluation quarantined to explicit dynamic or suspending seams that have not been removed yet.
- Prefer lowerer/emitter normalization over runner special-cases. If a suspending or nested-await shape can be rewritten into existing bytecode or dedicated IR instructions before execution, do that and delete the mixed AST/IR seam instead of preserving both paths.
- When attacking AST-eval seams, start by lowering evaluation-order-safe statement contexts first, then delete the now-dead suspending instruction family/handler. Treat "delete the seam" as the target, not "add another fallback".
- When compacting expression bytecode, push shared encodings into the runtime type that owns the semantics. Keep the execution fast path in encoded form and let printer/test bridges decode only for diagnostics.
- Interpreter side-state matters too. If expression execution needs per-stack metadata such as optional-chain short-circuit state, prefer packed representations over parallel byte/bool arrays when the semantics are binary.

## Lessons from failure-burn-down sessions
- Re-prove every subagent fix on `main` before claiming a reduction. A sidecar/worktree may pass because of local state, missing companion edits, stale binaries, or harness differences.
- If multiple unrelated Test262 cases fail with `ReferenceError` for obviously declared top-level names such as `ASCII_IDENTIFIER`, `other`, or `invalidStrings`, suspect a shared top-level binding or execution/harness bug before chasing feature-specific fixes.
- Do not widen shared validation helpers just to fix one edge-case cluster unless a broader proof run stays green. Prefer the narrowest path-specific fix that matches the failing behavior.
- Keep proof filters extremely narrow: exact failing file first, then owning cluster, then a slightly broader confirmation run.
- Subagent tasks should be tightly bounded: one seam, one owned file set, and one exact `Release` proof command.
- Copying a whole file from a sidecar is integration, not proof. Always rerun the exact focused proof on `main` immediately after transplanting a sidecar change.
- For IR/bytecode optimization work, keep the proof loop explicit: build, narrow owning pack, focused IR pack, AST-eval seam scan, and `./tools/profile forloop --memory`. Do not claim CPU or memory wins without current-worktree numbers.
