# ADR 0013: Keep slotless identifier assignment reference capture spec-ordered

## Status

Accepted

## Context

Issue #789 fixed the Test262 `IdentifierResolution` failure for
`language/identifier-resolution/assign-to-global-undefined.js`. The failing
shape was small, but it exposed a broader assignment-ordering trap in the
slotless IR expression-statement path.

ECMAScript simple assignment evaluates the left-hand reference before the right
hand side value. The slotless assignment handler only pre-resolved the left-hand
identifier reference when identifier caching was disabled for `with` or `eval`
scope. In ordinary script code, that let RHS side effects create or delete a
global object property before the final write decided whether the LHS was a
global binding or an unresolvable reference.

That ordering matters for strict code such as:

```javascript
"use strict";
undeclared = (this.undeclared = 5);
```

The left side is unresolvable at the assignment-reference step and must remain
that way even though the RHS creates a global property before `PutValue`.

The same issue also exposed that assignment-reference strictness cannot come
only from the current scope frame. The environment strict flag and strict source
context must be included so captured unresolvable/global references apply the
right strict/sloppy write behavior later.

## Decision

For expression-statement identifier assignments with no static flat slot and no
scoped slot, capture the `AssignmentReference` before evaluating the RHS.
Write back through that captured reference after RHS evaluation and signal
handling completes.

Keep the slot and scoped-slot fast paths direct when the analyzer already proved
the binding target. Only the slotless identifier path needs dynamic reference
capture because runtime global-object and unresolvable-reference state can be
observed by RHS side effects.

When building an `AssignmentReference`, compute strictness from the full active
execution context: environment strictness, current scope strictness, and strict
source context. Do not let a later write re-guess strictness from only the
current frame.

Do not repair this class of bug by adding an AST fallback or by converting
global constants such as `undefined`, `NaN`, and `Infinity` into writable
declarative bindings. They remain non-writable global object properties whose
ordinary write result is interpreted by strict or sloppy assignment semantics.

## Consequences

- Future assignment lowering must treat "resolve reference" and "evaluate RHS"
  as separate observable steps for slotless identifiers.
- RHS side effects that create or delete global properties must not change
  whether the LHS was originally resolvable.
- Strictness for captured references must be stored at reference construction
  time using the full environment/source context.
- Regression proof should include strict unresolvable assignment with RHS global
  creation, sloppy global `undefined` assignment, strict global `undefined`
  assignment, and the focused `Name=IdentifierResolution` Test262 group.
- This ADR is caused by issue #789 / PR #964 and complements ADR 0005's
  expression-bytecode assignment split, ADR 0011's spec-ordered binding target
  capture, and ADR 0012's expression-bytecode reference-target split.
