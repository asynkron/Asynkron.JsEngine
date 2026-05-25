# ADR 0138: Keep break/continue static validation shared and label-aware

## Status

Accepted

## Context

Issue #1836 / PR #1858 fixed Test262 `Statements_break` rows where illegal
`break` and `continue` shapes reached IR execution and failed with internal
control-flow target errors instead of parse-time syntax failures.

The fragile boundary was not just top-level `break`. The same static semantics
must also apply inside nested function and class bodies, while those bodies must
not inherit loop, switch, or label state from their enclosing source text.
Review then found another labeled-control-flow edge: in a nested labeled chain
such as:

```js
outer: inner: for (;;) {
    continue outer;
}
```

`continue outer` is valid because `outer` ultimately labels an iteration
statement. Classifying only the immediate labeled statement wrapper treats the
target as a non-iteration statement and rejects valid code.

Automatic semicolon insertion also matters. `break` followed by a line
terminator and then a label token is a valid unlabeled break plus a following
labeled expression statement, not `break label`.

## Decision

Keep illegal `break` and `continue` detection in a shared AST static
validation pass that runs before execution for ordinary script parsing and is
also reused by eval-specific validation.

The validator owns the ECMAScript control-flow static semantics:

- unlabeled `break` requires an enclosing iteration or switch statement;
- unlabeled `continue` requires an enclosing iteration statement;
- labeled `break` requires a label in the current function/class boundary;
- labeled `continue` requires a label whose ultimate target is an iteration
  statement;
- entering function or class bodies resets loop, switch, and label context; and
- nested `LabeledStatement` wrappers are unwrapped before target-kind
  classification.

Do not repair this class in IR lowering or execution by allowing missing
targets to become syntax errors. The execution pipeline should receive only
syntactically valid abrupt-control-flow statements.

Do not change parser ASI behavior to consume labels across line terminators for
`break` or `continue`. The parser decides statement shape; the validator
validates the statement shape it receives.

## Consequences

- Future control-flow static-semantics fixes should update the shared
  validator rather than duplicating script/eval checks.
- Any validator change must include positive and negative labeled coverage:
  illegal top-level or nested-function statements, valid nested-function local
  labels, and nested-label chains where the outer label targets a loop.
- ASI regressions need separate proof from illegal-control-flow regressions.
  A line terminator after `break` or `continue` can split the statement before
  validation sees it.
- Focused proof for this class is the internal `LabeledBreakContinueTests`
  pack plus targeted ASI coverage. Test262 confirmation should use the owning
  method group, for example `Name=Statements_break`, when the issue originates
  from Test262 rows.

## Related

- `.claude/rules/ecmascript-labeled-statements.md`
- `docs/adrs/0135-keep-labeled-let-lookahead-line-sensitive.md`
