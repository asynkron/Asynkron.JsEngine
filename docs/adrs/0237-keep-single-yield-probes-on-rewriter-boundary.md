# ADR 0237: Keep single-yield probes on the rewriter boundary

## Status

Accepted

## Context

Issue `autrun-ditjxyijy9e0-99946d8d6a` / PR #2405 was a recurring
code-reduction slice in `Ast/ShapeAnalyzer`. The owner surface had three
single-yield traversal concepts:

- `ShapeCounter`, which counts yields and descends into class heritage and
  computed member names.
- `SingleYieldLocator`, which found the first yield but did not descend into
  `ClassExpression`.
- `SingleYieldRewriter`, which rewrites the single yield and also does not
  descend into `ClassExpression`.

The first reduction attempt deleted `SingleYieldLocator` by storing the first
yield in `ShapeCounter`. Review sent it back because that changed
`TryFindSingleYield` traversal boundaries for class expressions. Restoring the
locator preserved behavior but failed the run's explicit code-reduction gates.
The accepted delivery deleted the locator and made `TryFindSingleYield` reuse
`SingleYieldRewriter` as a probe after the existing `AnalyzeExpression`
`YieldCount == 1` guard. A later review pass also removed the now-unused
`ShapeCounter.FirstYieldExpression` state.

Focused regressions now pin the intended boundary:
`TryFindSingleYield` ignores yields inside class `extends` expressions and
class computed member names.

## Decision

Keep single-yield discovery aligned with the rewriter traversal boundary.

`AstShapeAnalyzer.TryFindSingleYield` should keep using `AnalyzeExpression` as
the count guard, then use `SingleYieldRewriter` to discover the yield identity.
The rewritten expression produced by this probe is discarded, and the probe
symbol must not escape. Do not reintroduce a separate locator or store first
yield state in `ShapeCounter` unless the new owner proves the same traversal
boundary as `TryRewriteSingleYield`.

`ShapeCounter` remains a shape-counting traversal, not the owner of
single-yield identity. It can count yields in class heritage or computed member
names, but that does not require `TryFindSingleYield` to treat those nodes as
rewriteable single-yield candidates.

If this probe becomes performance-sensitive, extract a shared traversal owner
from the rewriter or split the rewriter into reusable traversal primitives with
the same boundary tests. Do not add another parallel AST walker whose node
coverage can drift independently.

## Consequences

- Single-yield discovery and rewriting share one traversal boundary.
- The code-reduction slice removes the 145-line locator without changing the
  class-expression behavior that review identified.
- Future AST shape reductions must compare traversal boundaries before merging
  walkers that look structurally similar.
- After review-driven rewrites, scan for state introduced by an earlier
  approach but no longer read by the final design.
- Focused proof for this surface should include
  `AstShapeAnalyzerSingleYieldTests` and a scan for `FirstYieldExpression` if
  `ShapeCounter` is touched.

## Related

- `.claude/rules/recurring-maintenance-child-runs.md`
- `src/Asynkron.JsEngine/Ast/ShapeAnalyzer/AstShapeAnalyzer.cs`
- `src/Asynkron.JsEngine/Ast/ShapeAnalyzer/ShapeCounter.cs`
- `src/Asynkron.JsEngine/Ast/ShapeAnalyzer/SingleYieldRewriter.cs`
- `tests/Asynkron.JsEngine.Tests/AstShapeAnalyzerSingleYieldTests.cs`
