# ADR 0267: Keep simple arrow unified bytecode routing benchmark-proven

## Status

Accepted

## Context

Issue `autrun-diuj48oxr9eg-a2279c89bf` / PR #2586 selected `classdef` from the
recurring optimizer benchmark table. The focused pre-edit row was:

```text
profile   asynkron_ms  jint_ms  delta
classdef          778      257  Jint 3.03x faster
```

The current profile again showed the final `dogs.map(d => d.speak())` callback
under `ArrayPrototype.Map`, `InvokeArrayIterationCallback`, and
`SyncFunctionInvoker.InvokeWithContextSlow`. ADR 0150 had already made this
simple arrow eligible for simple IR activation when its lowered return
`ExpressionProgram` has no lexical `this`, lexical `new.target`, or `super`
dependency.

This issue tested whether that same dependency predicate could also make simple
arrows eligible for the production unified bytecode function path. The attempted
change was deliberately narrow: arrow functions remained rejected unless their
simple return expression program contained no `this`, `new.target`, or `super`
operation. Focused array callback semantic tests passed while the edit was
present.

The selected benchmark regressed sharply:

```text
profile   asynkron_ms  jint_ms  delta
classdef         2957      965  Jint 3.06x faster
```

The runtime and test edits were reverted. The retained delivery was the
failed-attempt evidence note
`docs/performance/failed-classdef-arrow-unified-bytecode.md`.

## Decision

Do not treat simple-arrow lexical-dependency proof as sufficient evidence for
production unified bytecode routing.

Simple arrow callbacks may keep using the ADR 0150 simple IR activation path
when its bytecode-owned dependency guard passes. They should not be moved onto
the production unified bytecode route merely by relaxing
`CanUseProductionUnifiedBytecodeFastPath` to share that guard.

Future work may reopen this boundary only with a different owner hypothesis and
fresh proof. Acceptable follow-up directions include removing callback-call
setup directly, or adding a shape-specific path for the simple receiver method
call inside `d => d.speak()`, but either path must prove:

1. a current `classdef` CPU profile still names that owner;
2. focused semantic coverage keeps ordinary arrow lexical binding and method
   receiver binding intact;
3. repeated selected-profile rows improve rather than regress; and
4. the route does not add a broader mixed-execution fallback or a selector-side
   syntax shortcut that bypasses existing unified bytecode eligibility rules.

## Consequences

- ADR 0150 remains the positive decision for simple arrow IR activation; this
  ADR is the negative decision for reusing that predicate as production unified
  bytecode routing.
- A passing array-callback semantic test pack is not enough for this boundary.
  The selected performance row must also be measured before retaining code.
- Future optimizer agents should read the failed-attempt note before retrying
  `classdef` arrow callback routing, so the same guard relaxation is not
  repeated without new owner evidence.

## Related

- `docs/adrs/0150-keep-simple-arrow-ir-activation-lexical-dependency-guarded.md`
- `docs/performance/failed-classdef-arrow-unified-bytecode.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `.claude/rules/unified-bytecode-prototypes.md`
