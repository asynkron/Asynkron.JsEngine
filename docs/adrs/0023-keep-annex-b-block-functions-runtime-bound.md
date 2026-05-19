# ADR 0023: Keep Annex B block functions runtime-bound

## Status

Accepted

## Context

Issue #794 tracked eight Test262 `Language_functionCode` failures under
`annexB/language/function-code/*func-existing-fn-update.js`. The failing shape
was a sloppy-mode block-level function declaration with an existing
function-scoped binding of the same name.

Before PR #991, the IR path could treat direct block function declarations as
effectively hoisted no-ops when a block had no other lexical names. That lost the
Annex B runtime update step: the outer function binding must keep its prior
value until the block, branch, or switch case actually executes, then it must be
updated to the block function value. The optimized slot and flat-slot read paths
made this more subtle because stale function-scope, body-environment, or
flat-slot values could disagree with the executed declaration.

Strict mode and Annex B blocking rules still matter. A strict function, or a
sloppy function blocked by an intervening lexical binding, must not get the
sloppy Annex B var-binding update.

## Decision

Direct block-level function declarations that participate in block execution
must receive a real block-environment slot even when the block has no other
top-level lexical names. The block slot is the runtime source value that Annex B
can copy into the function's var binding after the declaration executes.

The IR runner's function declaration handler owns the Annex B update. In sloppy
mode, and only when the declaration is not blocked by the Annex B lexical checks,
it must update every representation that may be read afterward:

- the function or eval var-environment binding,
- intermediate body/block slots allocated for direct slot reads,
- flat-slot handles backed by those bindings.

The legacy AST path should mirror the same visible semantics for this slice so
that the result does not depend on whether the program selected IR execution or
AST walking.

## Consequences

- Future fixes for sloppy block function declarations should preserve runtime
  update timing instead of moving the inner function value into eager function
  instantiation.
- Block emitter changes must account for function declaration names separately
  from `let`/`const` TDZ names: function declarations need slots but should not
  be marked uninitialized for TDZ.
- Slot-optimized reads are part of the semantic surface. Updating only the
  named environment binding is not sufficient when body environments or
  flat-slot mappings were preallocated.
- Focused proof should include the `Name=Language_functionCode` Test262 method
  group plus local strict/sloppy block function coverage. Do not widen to the
  full Test262 suite for this issue class unless a separate investigation asks
  for it.
