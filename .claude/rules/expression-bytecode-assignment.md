# Expression Bytecode Assignment Semantics

When changing assignment operators, keep ECMAScript identifier assignment
semantics separate from member property write semantics across every execution
path that can evaluate the assignment.

## Rules

1. Enable assignment name inference only for identifier assignments whose RHS is
   an anonymous function, arrow, or class expression and whose source form is not
   excluded by the spec. Parenthesized identifier assignments such as
   `(target) = function() {}` must not infer `target`.
2. Apply the same parenthesized-assignment exclusion in every assignment path:
   expression bytecode, expression-statement slot assignment fast paths, and the
   quarantined legacy AST fallback. Do not fix only the bytecode compiler and
   leave another execution path applying the hint.
3. Pass `AllowNameInference: false` for all member, computed member, and super
   property writes. A `MemberExpression` assignment is a property write, not an
   identifier binding assignment.
4. For expression-position logical member assignments (`&&=`, `||=`, `??=`),
   prove both branches have the same stack contract: exactly one expression
   result remains, and duplicated receiver/property-key operands are cleaned up.
5. Add focused tests for the semantic split before widening Test262 proof:
   identifier name inference, parenthesized identifier no-inference, member
   no-inference, strict getter-only or non-writable write failures when the
   branch runs, and strict write skips when the logical assignment
   short-circuits.
6. For expression-statement identifier assignments with no flat slot and no
   scoped slot, capture the `AssignmentReference` before evaluating the RHS and
   write back through that captured reference afterward. RHS side effects that
   create or delete global properties must not change whether the LHS was
   originally resolvable.
7. Compute assignment-reference strictness from the full active context:
   environment strictness, current scope strictness, and strict source context.
   Do not derive it only from the current scope frame.
8. Only rewrite `x = x <op> rhs` into a compound-slot instruction after static
   slot resolution proves the RHS `x` and assignment target are the same
   binding. Emitter-time shortcuts must require already-stamped slot metadata;
   otherwise let `SlotAssignmentRewriter` decide after scope analysis. Dynamic
   lookup and no-cache paths, especially `with`, must keep generic assignment
   reference semantics. For `with` plus proxy-observable identifiers, guard both
   the trap/order behavior and the instruction shape; plain `p = p + rhs` must
   remain a distinct RHS read followed by writeback, not a compound-assignment
   shortcut.

## Why

Issue #782 / PR #931 fixed the `Expressions_logicalAssignment` Test262 cluster
after the expression bytecode path treated logical assignment too uniformly.
Identifier assignments needed RHS-based NamedEvaluation, but member writes had
to disable that same inference and preserve property write failures. The fix
also had to make expression-position member short-circuit cleanup leave the same
single-result stack shape as the write branch.

Issue #774 / PR #950 then exposed the same parenthesized-assignment exclusion on
plain assignment. The expression bytecode compiler, expression-statement slot
assignment path, and legacy AST fallback all needed the exclusion; otherwise one
path could still name an anonymous function assigned through `(identifier) =`.

Issue #789 / PR #964 fixed the `IdentifierResolution` Test262 fixture for
assigning to global `undefined`. The slotless expression-statement assignment
path resolved the LHS too late, so RHS side effects could create a global
property before the final write decided whether the original LHS was
unresolvable. The same repair confirmed that captured references must carry
strictness from environment/source context, not only the current scope frame.

Issue `autrun-dir08v6q4vag-367d2e753a` / PR #1701 optimized
self-referential arithmetic assignment lowering for the `ir-arithmetic`
profile, then review exposed that an emitter-only shape match could bypass
dynamic assignment lookup. The durable constraint is that assignment
optimizations are binding optimizations, not just syntax optimizations: the
rewrite is valid only after slot metadata proves both identifier references are
the same static slot.

Issue #gh1707 / PR #1710 confirmed the same boundary for plain
self-referential assignment under `with (proxy)`. The implementation was
already correct, but the missing regression guard showed why future changes
must prove both observable proxy trap order (`has` / unscopables / `get` /
final `set`) and IR shape (`AssignmentSlotInstruction`, not
`CompoundAssignmentSlotInstruction`) before claiming a dynamic assignment path
is safe to optimize.
