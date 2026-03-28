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

## Lessons from failure-burn-down sessions
- Re-prove every subagent fix on `main` before claiming a reduction. A sidecar/worktree may pass because of local state, missing companion edits, stale binaries, or harness differences.
- If multiple unrelated Test262 cases fail with `ReferenceError` for obviously declared top-level names such as `ASCII_IDENTIFIER`, `other`, or `invalidStrings`, suspect a shared top-level binding or execution/harness bug before chasing feature-specific fixes.
- Do not widen shared validation helpers just to fix one edge-case cluster unless a broader proof run stays green. Prefer the narrowest path-specific fix that matches the failing behavior.
- Keep proof filters extremely narrow: exact failing file first, then owning cluster, then a slightly broader confirmation run.
- Subagent tasks should be tightly bounded: one seam, one owned file set, and one exact `Release` proof command.
- Copying a whole file from a sidecar is integration, not proof. Always rerun the exact focused proof on `main` immediately after transplanting a sidecar change.
