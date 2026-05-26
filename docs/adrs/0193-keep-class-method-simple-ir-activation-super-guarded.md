# ADR 0193: Keep class method simple IR activation super-guarded

## Status

Accepted

## Context

Issue `autrun-disrk4l293k0-661ecf4a43` / PR #2175 selected `classdef` from the
optimizer automation baseline. The baseline showed Asynkron at 1807 ms while
Jint was at 682 ms. A focused CPU profile still showed constructor and
`super()` dispatch as the largest cost, but the `dogs.map(d => d.speak())`
tail also repeatedly entered typed function invocation for a plain class
method.

Earlier `classdef` slices had already narrowed array callback carriers and
allowed simple arrows onto the simple IR activation path when their lowered
return program had no lexical `this`, `new.target`, or `super` dependency.
This slice found the next boundary: ordinary class methods were still rejected
from simple IR activation solely because they had a home object.

A home object is required for methods whose body can execute `super`, but it is
not by itself an observable activation requirement for a plain simple-return
method. Ordinary method `this` binding is already supplied by the simple
activation environment; `super` access is the dependency that must keep the
method on the full invocation path.

## Decision

Keep class method simple IR activation eligible only when the already-lowered
simple return `ExpressionProgram` proves the method has no `super` dependency.

The eligibility boundary is:

1. the existing simple IR activation base and activation-slot shape checks must
   still pass;
2. class constructors, async functions, generators, parameter expressions,
   `arguments`, captured activation, dynamic identifier-cache cases,
   private-name scopes, explicit super constructor/prototype state, and
   instance fields must continue using the existing fallbacks;
3. a method with no home object remains eligible under the existing ordinary
   simple activation checks;
4. a method with a home object additionally needs a simple return program; and
5. that program must contain no `ExpressionOpKind` that loads a super call
   target, ensures a super reference, gets/sets/updates a super property, or
   constructs via `super`.

Do not replace this with a method-name, benchmark-name, source-size, or
callback-shape heuristic. The lowered expression program is the proof surface
because it is the executable payload the fast path evaluates.

Do not treat ordinary method `this` as the same dependency as arrow lexical
`this`. A class method may load its receiver through simple activation, while a
simple arrow must still prove it has no lexical `this` dependency as recorded
in ADR 0150.

## Consequences

- Plain simple-return class methods can reuse the existing simple IR activation
  path without erasing the semantic distinction between receiver binding and
  `super` binding.
- Methods with `super` operations continue through the full invocation path
  that owns home-object semantics.
- Adding new expression bytecode operations for `super` must update the
  super-operation eligibility scan before those methods can use the shortcut.
- Future activation work touching this boundary should pair selected-profile
  evidence with focused class/super semantics tests, array-callback coverage
  for the `classdef` map tail, the AST-eval seam scan, and the activation proof
  pack when activation semantics are widened.
- The performance note
  `docs/performance/classdef-homeobject-simple-ir-activation.md` remains the
  measurement evidence; this ADR records the semantic policy.

## Related

- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `docs/adrs/0150-keep-simple-arrow-ir-activation-lexical-dependency-guarded.md`
- `docs/adrs/0176-keep-sync-ir-activation-environment-pooling-ownership-guarded.md`
- `docs/performance/classdef-homeobject-simple-ir-activation.md`
