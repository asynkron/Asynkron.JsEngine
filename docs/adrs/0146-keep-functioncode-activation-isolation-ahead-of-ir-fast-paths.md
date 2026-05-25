# ADR 0146: Keep FunctionCode activation isolation ahead of IR fast paths

## Status

Accepted

## Context

Issue #1866 / PR #1921 fixed the remaining Test262
`language/function-code` execution-context rows after the reduced residual set
showed three failures:

- `S10.2.1_A4_T1.js` in sloppy mode;
- `S10.4A1.1_T2.js` in sloppy mode; and
- `S10.4A1.1_T2.js` in strict mode.

The delivery started with a context lifetime repair, then proved that the
remaining failures were not a harness issue. The affected surface was the sync
function invocation boundary where script-mode FunctionCode semantics,
function-declaration hoisting, recursive calls, invocation-environment pooling,
and sync IR trampoline eligibility meet.

The difficult part was not simply disabling IR. Broad script-mode IR opt-outs
fixed the FunctionCode rows but regressed strict proper-tail-call behavior by
routing eligible recursive calls away from the tail-call path and into ordinary
stack growth. The final delivery instead kept the predicates narrow:

- function declaration names that conflict with parameters or the sloppy
  `arguments` binding can force the script-mode FunctionCode seam away from the
  IR path;
- self-recursive non-parameter identifier calls can observe activation/context
  reuse and must not use invocation-environment pooling or the current sync IR
  trampoline when hoistable declarations make isolation necessary;
- unrelated strict same-function tail recursion must remain eligible for the
  proven proper-tail-call path.

## Decision

Keep FunctionCode activation isolation as a first-class eligibility boundary
for sync function fast paths.

When changing sync function IR activation, invocation-environment pooling, or
sync IR trampoline eligibility, classify FunctionCode risk by the observable
activation shape instead of by a broad "script-mode" or "recursive" flag.

The current policy is:

1. function-declaration/parameter-name conflicts, including the sloppy
   `arguments` binding case, are a FunctionCode instantiation seam and may
   require ordinary activation handling;
2. self-recursive identifier calls are unsafe for pooling/trampoline reuse when
   the activation can be observed through hoistable declarations or equivalent
   FunctionCode context state;
3. non-conflicting strict same-function tail calls must keep the proper-tail
   call path when the trampoline/restart executor can model the shape; and
4. context lifetime is owned by the outer invocation cleanup path. Nested IR
   fast-path branches must not return the same `EvaluationContext` separately.

Do not repair FunctionCode rows by moving them into legacy AST evaluation as a
default path, and do not repair strict tail-call stack growth by weakening
FunctionCode activation isolation. The eligibility predicates should explain
which observable binding or context state would be reused incorrectly.

## Consequences

- Test262 FunctionCode residuals stay tied to the function-entry and
  declaration-instantiation owner surface instead of the harness.
- IR fast paths remain available for safe strict tail recursion and simple
  activation shapes.
- Invocation-environment pooling and sync IR trampolines must reject recursive
  shapes that can observe reused activation state.
- Future changes need paired proof when they touch this boundary:
  `Name=FunctionCode` for FunctionCode semantics and the focused strict
  same-function tail-call proof for stack-depth stability.
- This ADR narrows, but does not replace, the existing activation-slot and
  proper-tail-call ADRs. Activation metadata still owns slot shape, and the
  tail-call runtime still owns receiver/context/cleanup preservation.

## Related

- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/proper-tail-calls.md`
- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `docs/adrs/0126-keep-proper-tail-calls-runtime-context-owned.md`
- `docs/adrs/0140-keep-sync-ir-trampoline-eligibility-executor-exact.md`
