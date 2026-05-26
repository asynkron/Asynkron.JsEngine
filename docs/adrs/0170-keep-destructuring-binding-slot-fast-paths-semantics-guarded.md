# ADR 0170: Keep destructuring binding slot fast paths semantics-guarded

## Status

Accepted

## Context

Issue #2054 / PR #2070 targeted the `destructuring` benchmark after earlier
work moved scope-entry TDZ marking and dense-array iterator guards out of the
hottest path. The next visible owner was binding-pattern overhead where
identifier binding targets already had analyzer/lowering-proven slot metadata.

The accepted implementation stamps `IdentifierBindingTargetProgram` with
`ScopeId`, `SlotIndex`, and `FlatSlotId`, extends array destructuring IR element
and rest instructions with target slot indices, and lets the runner write
directly for proven lexical let/const binding shapes. It keeps unstamped,
dynamic, var, special-binding, and unsupported shapes on the existing generic
binding path.

The delivery required several review/build-back repairs:

- catch binding target programs had to be rewritten while the catch scope was
  already on the scope stack, otherwise a catch destructuring target could be
  stamped as an outer lexical slot and initialize the wrong binding;
- a regression assertion used an array input for an object binding metadata
  proof, producing the expected JavaScript `NaN` rather than proving slot
  metadata on `{ x, y }`;
- the direct assignment slot fast path initially skipped immutable binding and
  global constant semantics. Named function expression self-bindings and global
  constants must keep the generic assignment behavior.

The final delivery added focused proof for stamped object binding target
programs, array destructuring IR target slots, catch-scope provenance, const
preservation, custom iterator fallback, iterator close on abrupt assignment, and
sloppy/strict named function expression self-binding assignment.

## Decision

Keep destructuring binding slot fast paths as metadata-proven and
runtime-semantics guarded.

The stamping owner is `SlotAssignmentRewriter`, after scope analysis has
resolved the relevant binding. It may stamp identifier binding target programs
and simple array destructuring element/rest targets with slot metadata only when
`TryResolve` proves the binding in the current scope stack. For catch bindings,
the catch scope must be visible on that stack before rewriting the catch
binding program.

The runner may consume stamped metadata only after validating the runtime
binding still matches the stamped shape:

1. the flat slot id exists and points at a valid variable;
2. the variable environment has the expected scope id;
3. the resolved slot's name is the target symbol;
4. assignment mode is still cache-safe and not inside a `with` chain; and
5. the target slot is not an immutable binding, global constant, special
   binding, or another shape whose generic assignment path owns semantics.

For declaration binding, direct slot writes are limited to lexical let/const
targets that are still uninitialized and non-special. Var binding and any
unsupported slot state must fall back to `DefineJsValue`,
`EnsureFunctionScopedVarBinding`, `AssignJsValue`, or the existing generic
binding helper.

Do not treat slot metadata as permission to bypass iterator protocol,
ToObject/coercion, target resolution ordering, TDZ/const errors, immutable
function-name binding behavior, global constant behavior, or dynamic lookup
guards.

## Consequences

- Future destructuring performance work can keep moving repeated symbol lookup
  out of hot binding paths, but only when the plan/stamping layer proves the
  slot and the runner revalidates runtime semantics before writing.
- Catch destructuring is a scope-provenance boundary: stamp catch target
  programs after entering the catch scope in the rewriter, and keep regressions
  that prove an outer lexical binding is not initialized by the catch target.
- Assignment destructuring fast paths need both positive metadata tests and
  negative semantic tests for const, immutable function-name bindings, global
  constants or equivalent special bindings, and `with`/dynamic fallback.
- Dense array destructuring remains governed by ADR 0154. Slot-proven target
  writes do not widen the iterator bypass boundary.
- If a direct-slot binding path needs a new semantic exception, prefer falling
  back to the generic binding helper over duplicating more binding semantics in
  the fast path.

## Related

- `docs/adrs/0011-keep-destructuring-binding-target-resolution-spec-ordered.md`
- `docs/adrs/0107-keep-self-referential-assignment-slot-optimization-slot-proven.md`
- `docs/adrs/0142-keep-scope-entry-tdz-slot-marking-plan-owned.md`
- `docs/adrs/0154-keep-dense-array-destructuring-fast-path-iterator-observable.md`
- `.claude/rules/js-spec-property-access.md`
