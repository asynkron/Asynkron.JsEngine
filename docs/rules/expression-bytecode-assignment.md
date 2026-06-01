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
   identifier binding assignment. Non-computed private member assignments are
   still named member writes, but reads and writes must stay private-aware:
   getter/setter calls, brand checks, and short-circuit skips belong to the
   property handle path, not to a plain-object fallback or an AST-evaluation
   escape hatch.
4. For expression-position logical member assignments (`&&=`, `||=`, `??=`),
   prove both branches have the same stack contract: exactly one expression
   result remains, and duplicated receiver/property-key operands are cleaned up.
5. Add focused tests for the semantic split before widening Test262 proof:
   identifier name inference, parenthesized identifier no-inference, member
   no-inference, strict getter-only or non-writable write failures when the
   branch runs, strict write skips when the logical assignment short-circuits,
   and private accessor branches where `&&=`, `||=`, and `??=` invoke the
   private setter only when assignment actually runs.
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
9. When refreshing roadmap or planning docs for assignment-lowering work, state
   both halves of the boundary: slot-proven static paths may use the
   compound-slot optimization, while dynamic `with`/proxy/no-cache identifier
   semantics remain on generic assignment-reference paths. Cite the maintained
   performance note and ADR guardrails instead of implying a new benchmark,
   Test262, or runtime win unless the current slice actually re-proved it.
10. When admitting a new compound property write shape to the unified-bytecode
    production path, simultaneously update the boundary description in
    `docs/unified-bytecode-expansion-contract.md`. Name the newly admitted shape,
    its constraints (activation-resolved base, named key, production binary
    operator, simple RHS), and the retained declines that are still true for
    the current boundary, such as optional/private/dynamic neighbors, unowned
    computed-key spans, or unsupported RHS shapes. Do not leave the old blanket
    "is declined" statement intact, and do not keep historical retained-decline
    examples after a later slice admits them — stale decline descriptions
    mislead agents into thinking a shape is still excluded when it was already
    admitted.

## Why

Issue #782 / PR #931 fixed the `Expressions_logicalAssignment` Test262 cluster
after the expression bytecode path treated logical assignment too uniformly.
Identifier assignments needed RHS-based NamedEvaluation, but member writes had
to disable that same inference and preserve property write failures. The fix
also had to make expression-position member short-circuit cleanup leave the same
single-result stack shape as the write branch.

Issue #1832 / PR #1857 added the missing private-accessor regression slice for
that same member path. The durable lesson is that private accessor logical
assignment is not a separate lowering family, but it must prove the named member
path routes reads and writes through private-aware `PropertyHandle` semantics:
assignment branches call the private setter, while short-circuit branches skip
both RHS evaluation and the setter.

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

Issue `autrun-dir45gsowoq8-58782a260a` / PR #1732 was a docs-only roadmap
refresh after the implementation work had already landed. The useful lesson was
not another assignment optimization: it was keeping the roadmap evidence-backed
by naming the optimized static-slot path, the preserved dynamic lookup boundary,
and the existing proof surfaces (`docs/performance/ir-arithmetic-self-assignment-compound-slot.md`,
ADR 0107, and ADR 0108) without claiming new verification from a maintenance
slice that did not run runtime proofs.

Issue `planitem-planmanual1780157100924814000-baseline-batch-4-compound-property-writes` /
PR #2756 corrected a stale sentence in `docs/unified-bytecode-expansion-contract.md`
that described compound read-then-write as "declined by `PropertyWriteDependency`"
after the named-property shape had already been admitted by
`TryIsFirstBoundaryNamedCompoundPropertyWriteCandidate`. The doc was accurate when
originally written but became stale when eligibility expanded. The durable
constraint is rule 10 below.
