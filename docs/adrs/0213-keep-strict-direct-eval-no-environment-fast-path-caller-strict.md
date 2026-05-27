# ADR 0213: Keep strict direct eval no-environment fast path caller-strict

## Status

Accepted

## Context

Issue #2257 / PR #2262 continued the `activation-evalscope-lite` follow-up
after the eval program cache, last-entry cache, and ADR 0206's declaration-free
strict direct-eval environment reuse had landed.

The selected CPU profile still showed direct eval activation/setup overhead:

```text
Baseline timestamp: 2026-05-27T05:16:45Z
InvokeWithContextSlow call-tree root total = 530.35 ms
```

The repeated workload executes strict direct eval inside an already-strict
caller and the parsed eval source has no top-level declarations. In that shape,
the fresh strict direct-eval lexical environment from ADR 0206 owns no bindings:
identifier reads and writes resolve through to the caller environment, and the
caller environment is already strict.

The semantic risk is still high. Direct eval can observe current activation
bindings, `arguments`, `new.target`, `super`, private-name scopes, and
class-field initializer state. Strictness may come from the eval source rather
than the caller, and declaration-bearing strict eval still needs declaration
instantiation isolation so eval declarations do not leak into the caller.

## Decision

Declaration-free strict direct eval may skip creating both the strict direct
lexical environment and the eval declaration environment only when all of these
conditions are proven after parsing and declaration collection:

1. the call is direct eval;
2. the eval program is strict;
3. the caller was already strict before eval;
4. top-level var-declared names are empty;
5. top-level lexical declarations are empty; and
6. top-level var-scoped function declarations are empty.

The fast path must still run the normal eval parse/cache lookup, strict-reserved
binding validation, control-flow validation, `super` / `new.target` validation,
and private-name validation before executing the program. It may execute the
program directly in the current caller environment with `ExecutionKind.Eval`
and inherited private-name scopes.

Do not mark the caller environment as an eval declaration environment on this
path. There are no eval declarations to instantiate, and marking the caller
would make an optimization mutate activation state that the ordinary
declaration-free path does not need.

If the caller is not already strict, even when the eval source itself is strict,
keep ADR 0206's fresh strict direct-eval lexical environment path. If the eval
program has any top-level `var`, function, `let`, `const`, or class
declaration, keep the existing eval declaration-instantiation path. Sloppy
direct eval and indirect eval stay on their existing paths.

Do not replace the predicate with source-text heuristics, benchmark-name checks,
or a broad "strict eval has no local bindings" rule.

## Consequences

- Declaration-free strict direct eval in an already-strict caller avoids an
  empty environment allocation and avoids mutating the caller as an eval
  declaration environment.
- Current caller activation bindings remain live and observable across repeated
  eval calls, closures, and binding changes.
- Declaration-bearing strict direct eval remains isolated from the caller.
- Strict eval source executed from a sloppy caller remains on the fresh lexical
  environment path from ADR 0206.
- The retained delivery moved the selected profile root from `530.35 ms` to
  `505.49 ms` (`-24.86 ms`). `EvalHostFunction.InvokeSingleArgument` dropped
  from `82.93 ms` in the baseline top-functions table to `21.10 ms` under the
  follow-up direct-eval call tree.
- Future work in this area must prove the no-environment fast path and the
  declaration-bearing fallback with focused eval/activation/class-element
  coverage, then report selected `activation-evalscope-lite` before/after
  evidence.

## Related

- Issue #2257 / PR #2262
- `docs/performance/activation-evalscope-eval-program-last-entry-cache.md`
- `docs/adrs/0015-keep-direct-eval-caller-lexical-context.md`
- `docs/adrs/0132-keep-direct-eval-var-arguments-collision-checks-narrow.md`
- `docs/adrs/0185-keep-direct-eval-program-cache-strictness-and-caller-context-owned.md`
- `docs/adrs/0206-keep-strict-direct-eval-declaration-free-environment-reuse.md`
- `.claude/rules/ecmascript-direct-eval-declaration-instantiation.md`
- `.claude/rules/performance-profiling-guardrails.md`
