# ADR 0075: Keep switch async/generator declarations eval-aware

## Status

Accepted

## Context

Issue #1069 / PR #1241 fixed the focused Test262 `Statements_switch`
failures for sloppy-mode switch clauses containing `async function`,
`async function*`, or `function*` declarations. The failing fixtures expected
those declarations to stay scoped to the switch lexical environment so reading
the name after the switch throws `ReferenceError`.

The existing Annex B block-function runtime update path was too broad for this
shape. Ordinary sloppy block-level `function` declarations may update an
existing var binding when the block executes, but async and generator function
declarations in switch clauses must not leak through that outer binding. The
initial repair suppressed the Annex B var update for async/generator
declarations, which fixed the Test262 switch group but regressed direct and
indirect eval switch declaration behavior in the quality gate.

The re-entry fix made the suppression execution-kind aware:
async/generator switch declarations suppress the Annex B var-binding update in
ordinary non-eval execution, while eval declaration environments keep their
existing var-binding update semantics.

## Decision

Keep async/generator function-declaration Annex B suppression tied to the active
execution kind. In the IR declaration handler, do not let ordinary sloppy
switch or catch execution update the enclosing var binding for:

1. `async function x() {}`,
2. `async function* x() {}`, or
3. `function* x() {}`.

Do preserve eval declaration semantics by allowing those declarations to update
the eval/global var binding when the active execution kind is eval.

Future changes in this area must not key the behavior only on declaration kind,
syntactic container, or var-environment identity. The observable behavior also
depends on whether the declaration is executing as eval code.

## Consequences

- Sloppy switch/catch declaration fixes must distinguish ordinary runtime
  execution from eval execution before changing Annex B var-binding updates.
- Regression coverage should include the focused `Name=Statements_switch`
  Test262 method group, local sloppy switch async/generator scoping coverage,
  and eval switch async/generator var-update coverage.
- Do not repair this class by moving switch async/generator declarations into
  eager function-scope hoisting, by editing Test262 harness policy, or by
  disabling Annex B updates for all block-level function declarations.
- This ADR is caused by issue #1069 / PR #1241 and complements ADR 0023 plus
  the root `.claude/rules/ecmascript-annex-b-block-functions.md` rule.
