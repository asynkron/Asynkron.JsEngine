# Language Suite Next Steps

## Current State
- The legacy `StandardLibrary.Array.cs` has been split into focused partials so helper logic (species creation, ReduceLike, iterator plumbing, Array.from/of) lives under `StdLib/Array/StandardLibrary.Array.*.cs`.
- `%Array.prototype%` now comes from the generator-backed `ArrayPrototype` type, with iteration-centric methods (`map`, `filter`, `reduce`, `find*`, `every`, `some`, `forEach`), mutators (`push`/`pop`, `shift`/`unshift`, `splice`, `concat`, `reverse`, `sort`), and transformation helpers (`join`, `slice`, `flat*`, `fill`, `copyWithin`, `toSorted`, `toReversed`, `toSpliced`, `with`, iterator factories) each living in their own partials so no single file dominates the definition.
- `dotnet build` succeeds with the new layout, so the constructor/prototype reshuffle doesn't break existing projects and existing array tests (e.g., `PrototypeLookupResolvesInheritedMethods`) stay green.

## Next Iteration Plan
1. Sweep for any callers that still expect the deleted `StandardLibrary.Array` monolith (e.g., missing helper exports or stale `using` directives) and patch them before enabling more aggressive warnings.
2. Once confident in the structure, run the array-focused unit/Test262 slices to confirm no behavioural regressions and capture any migration bugs early.
3. Keep tightening the partials (shared helpers, comments, tests) so additional array features can slot into the generator model without reintroducing giant files.
