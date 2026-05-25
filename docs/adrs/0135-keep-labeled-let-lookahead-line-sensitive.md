# ADR 0135: Keep labeled `let` lookahead line-sensitive

## Status

Accepted

## Context

Issue #1839 / PR #1872 fixed Test262 `Statements_labeled` rows where labeled
declaration parsing crashed or rejected the wrong sloppy-mode shape.

The parser already rejected labeled lexical declarations such as
`label: const x = 1;` and `label: class C {}`. The fragile part was `let`,
because sloppy-mode JavaScript can still use `let` as an identifier in an
expression statement. A labeled statement beginning with `let` therefore needs
the same line-sensitive lookahead boundary as ordinary statement parsing:

```js
label: let
x = 1;
```

is not the same syntactic shape as:

```js
label: let x = 1;
```

The delivery was review-bounced because a broad labeled-lexical guard treated
`let` lookahead as declaration evidence even when a line terminator separated
`let` from the following token. That overreached the sloppy identifier path and
failed the focused labeled Test262 gate until the `let` branch checked for no
line terminator before applying `{`, `[`, or binding-identifier lookahead.

## Decision

Keep labeled statement lexical-declaration rejection in the parser, but make
the `let` branch line-sensitive.

For labeled statements:

- always reject `const` and `class` after the label;
- always reject `let` after the label in strict context;
- in sloppy context, reject `let` as a labeled lexical declaration only when
  the next token is on the same line and the ordinary lexical-declaration
  lookahead proves `{`, `[`, or a binding identifier; and
- preserve Annex B labeled function behavior separately: sloppy labeled
  functions are allowed, strict labeled functions and labeled generators are
  rejected.

Do not solve this class in runtime evaluation, IR lowering, Test262 harness
policy, or generated Test262 files. The bug is a parser static-semantics and
automatic-semicolon-insertion boundary, and the parser is the only layer that
has both token lookahead and strict/sloppy context.

## Consequences

- Future labeled-statement parser changes must audit line terminator handling
  whenever a contextual keyword can also be an identifier in sloppy mode.
- A broad "lexical declaration after label" guard is too coarse for `let`;
  strictness and same-line lookahead are part of the rule.
- Focused proof for this class should include local labeled declaration
  regressions plus the owning Test262 method group `Name=Statements_labeled`.
- This ADR is caused by issue #1839 / PR #1872 and is enforced by
  `.claude/rules/ecmascript-labeled-statements.md`.
