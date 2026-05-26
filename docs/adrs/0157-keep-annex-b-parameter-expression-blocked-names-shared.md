# ADR 0157: Keep Annex B parameter-expression blocked names shared

## Status

Accepted

## Context

Issue #2001 / PR #2009 fixed the Test262
`annexB/language/function-code/*-func-skip-dft-param.js` cluster. The failing
shape was a sloppy function whose parameter list has parameter expressions,
combined with Annex B block-level function declarations in the function body.

For this shape, Annex B var-style function hoisting must be skipped. Before the
delivery, the engine only blocked selected names that conflicted with
parameters, body lexical declarations, non-simple catch parameters, or
`arguments`. That left block function names eligible for an undefined
var-style hoist artifact and, on the IR path, for var-scoped slot allocation
even though the legacy Annex B hoist should not exist for
parameter-expression functions.

The implementation touched several parallel surfaces:

- IR slot planning in `ExecutionPlanBuilder`;
- execution-runner function environment setup;
- sync function invocation setup;
- legacy hoist-time undefined binding creation; and
- runtime declaration execution, which remains the update point for ordinary
  unblocked Annex B block functions.

Review then found a coverage gap: the new blocked-name collectors did not
unwrap `LabeledStatement`, while the existing hoisted-function collector did.
That mismatch meant a label-wrapped block function could still bypass the new
blocked-name rule and receive stale var-scope backing storage.

## Decision

Treat Annex B block-function skipping for parameter-expression functions as a
shared FunctionDeclarationInstantiation decision, not as a local runtime guard.

When a sloppy function has parameter expressions, collect every Annex B-eligible
block function name from the function body and add it to the blocked-name set
before var-style hoisting or var-slot allocation can happen. The same semantic
decision must be reflected in:

1. IR slot assignment, so skipped block functions do not receive stale
   var-scoped slots;
2. execution-runner environment setup, so runtime declaration handling knows
   which Annex B updates are blocked;
3. sync invocation setup, so the non-runner function path uses the same
   blocked-name set; and
4. legacy hoisting, so the hoist-time undefined var binding is not created for
   blocked Annex B names.

Collectors that find Annex B block-function names must stay traversal-compatible
with the hoisted-function collectors they constrain. In particular, transparent
wrappers such as `LabeledStatement` are unwrapped in every duplicated collector.
Future statement-wrapper additions should compare both collector families before
claiming the semantic surface is covered.

Do not use this rule to suppress ordinary sloppy Annex B block-function runtime
updates, strict-mode block scoping, async/generator exclusions, or eval-specific
var-binding behavior. Those remain owned by the existing Annex B runtime-bound
policy.

## Consequences

- Parameter-expression FunctionCode issues now have a single owner surface:
  function declaration instantiation plus IR/legacy hoist alignment.
- Slot planning is part of the observable behavior for this class. Blocking only
  the runtime update is insufficient if a stale var-scope slot was already
  allocated.
- Duplicated collectors are acceptable here, but traversal drift is a bug. Label
  unwrapping and future transparent statement wrappers must be handled in every
  copied collector or extracted into a shared helper.
- Focused proof should include the exact
  `annexB/language/function-code/*-func-skip-dft-param.js` rows, a Release
  build, and a targeted inspection or regression for wrapper traversal when a
  collector changes.

## Related

- `.claude/rules/ecmascript-annex-b-block-functions.md`
- `.claude/rules/function-activation-proof-pack.md`
- `docs/adrs/0023-keep-annex-b-block-functions-runtime-bound.md`
- `docs/adrs/0146-keep-functioncode-activation-isolation-ahead-of-ir-fast-paths.md`
