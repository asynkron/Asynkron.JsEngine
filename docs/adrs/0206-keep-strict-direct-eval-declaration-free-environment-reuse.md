# ADR 0206: Keep strict direct eval declaration-free environment reuse narrow

## Status

Accepted

## Context

Issue #2228 / PR #2241 reduced residual `activation-evalscope-lite` overhead
after the eval program last-entry cache work. The selected profile repeatedly
called strict direct eval with a declaration-free expression such as
`eval("y + shared")`.

Strict direct eval already creates a fresh strict direct-eval lexical
environment before `EvalDeclarationInstantiation`. The previous runtime then
created a second empty `eval` child environment even when the parsed eval
program had no top-level `var`, function, `let`, `const`, or class
declarations. That extra child environment added activation/environment setup
cost without owning any bindings for declaration-free eval code.

The risk is semantic, not mechanical. Direct eval can observe caller activation
state such as `arguments`, `new.target`, `super`, private-name scopes, and
class-field initializer context. Declaration-bearing eval programs also rely on
the declaration-instantiation environment boundary to prevent strict eval
bindings from leaking to the caller and to keep sloppy/direct/indirect eval
behavior split.

## Decision

Strict direct eval may reuse the already-created strict direct-eval lexical
environment as the eval execution environment only when the eval program is
declaration-free.

The declaration-free predicate must be computed after parsing and declaration
collection, and it must require all of these to be empty:

1. top-level var-declared names;
2. top-level lexical declarations, including class declarations; and
3. top-level var-scoped function declarations.

Declaration-bearing strict direct eval must keep the existing child
`description: "eval"` environment path. Sloppy direct eval and indirect eval
must stay on their existing paths.

Do not replace this with a source-text heuristic, benchmark-name shortcut, or
generic "strict eval is isolated" rule. The reusable environment is the fresh
strict direct-eval lexical environment created for this eval call, not the
caller environment. ADR 0213 later defines the narrower already-strict caller
no-environment carve-out; this ADR remains the one-environment strict eval path
when that carve-out is not active.

The selected eval execution environment, whether reused or newly created, must
still be marked as the eval declaration environment before declaration
instantiation runs.

## Consequences

- Declaration-free strict direct eval avoids an empty child environment while
  preserving the fresh strict direct-eval lexical boundary.
- Eval programs with `var`, function, `let`, `const`, or class declarations
  remain isolated from the caller by the child eval environment.
- Future eval activation optimizations must prove both the fast path and the
  declaration-bearing fallback. The focused internal proof should include
  `EvalFunctionTests`, `ActivationSemanticsProofPackTests`, and
  `ClassElementEvalTests` when direct-eval environment depth changes.
- Performance claims for this surface need selected `activation-evalscope-lite`
  evidence. For #2228, the documented reference was `1700ms`; the retained
  delivery measured `511ms` for `rtk ./benchmark.sh activation-evalscope-lite`
  with 86 focused tests passing.

## Related

- Issue #2228 / PR #2241
- Issue #2149
- `docs/performance/activation-evalscope-eval-program-last-entry-cache.md`
- `docs/adrs/0015-keep-direct-eval-caller-lexical-context.md`
- `docs/adrs/0132-keep-direct-eval-var-arguments-collision-checks-narrow.md`
- `docs/adrs/0213-keep-strict-direct-eval-no-environment-fast-path-caller-strict.md`
- `.claude/rules/ecmascript-direct-eval-declaration-instantiation.md`
