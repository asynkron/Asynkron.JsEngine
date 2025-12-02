# Language Suite Next Steps

## Current State
- The legacy `StandardLibrary.Array.cs` has been split into focused partials so helper logic (species creation, ReduceLike, iterator plumbing, Array.from/of) lives under `StdLib/Array/StandardLibrary.Array.*.cs`.
- `%Array.prototype%` now comes from the generator-backed `ArrayPrototype` type, with iteration-centric methods (`map`, `filter`, `reduce`, `find*`, `every`, `some`, `forEach`), mutators (`push`/`pop`, `shift`/`unshift`, `splice`, `concat`, `reverse`, `sort`), and transformation helpers (`join`, `slice`, `flat*`, `fill`, `copyWithin`, `toSorted`, `toReversed`, `toSpliced`, `with`, iterator factories) each living in their own partials so no single file dominates the definition.
- `dotnet build` succeeds with the new layout, so the constructor/prototype reshuffle doesn't break existing projects and existing array tests (e.g., `PrototypeLookupResolvesInheritedMethods`) stay green.
- Function declarations no longer re-instantiate at runtime (evaluation is now a no-op), which fixed the Array.prototype.every subclass failures when a function declaration was overwriting a custom prototype assignment.

## Next Iteration Plan
1. Tidy up the focused array tests and harness helpers now that the subclass/hoisting bug is fixed; keep the targeted `Array_prototype_every` filter test for quick iteration.
2. Keep tightening the partials (shared helpers, comments, tests) so additional array features can slot into the generator model without reintroducing giant files; remove the temporary logging once the remaining typed array slices are green.
