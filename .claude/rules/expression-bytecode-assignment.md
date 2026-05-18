# Expression Bytecode Assignment Semantics

When changing expression bytecode for assignment operators, keep ECMAScript
identifier assignment semantics separate from member property write semantics.

## Rules

1. Enable assignment name inference only for identifier assignments whose RHS is
   an anonymous function, arrow, or class expression and whose source form is not
   excluded by the spec, such as parenthesized assignment.
2. Pass `AllowNameInference: false` for all member, computed member, and super
   property writes. A `MemberExpression` assignment is a property write, not an
   identifier binding assignment.
3. For expression-position logical member assignments (`&&=`, `||=`, `??=`),
   prove both branches have the same stack contract: exactly one expression
   result remains, and duplicated receiver/property-key operands are cleaned up.
4. Add focused tests for the semantic split before widening Test262 proof:
   identifier name inference, member no-inference, strict getter-only or
   non-writable write failures when the branch runs, and strict write skips when
   the logical assignment short-circuits.

## Why

Issue #782 / PR #931 fixed the `Expressions_logicalAssignment` Test262 cluster
after the expression bytecode path treated logical assignment too uniformly.
Identifier assignments needed RHS-based NamedEvaluation, but member writes had
to disable that same inference and preserve property write failures. The fix
also had to make expression-position member short-circuit cleanup leave the same
single-result stack shape as the write branch.
