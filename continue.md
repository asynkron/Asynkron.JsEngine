# Language Suite Next Steps

## Current State
- The legacy `StandardLibrary.Array.cs` has been split into focused partials so helper logic (species creation, ReduceLike, iterator plumbing, Array.from/of) lives under `StdLib/Array/StandardLibrary.Array.*.cs`.
- `%Array.prototype%` now comes from the generator-backed `ArrayPrototype` type, with iteration-centric methods (`map`, `filter`, `reduce`, `find*`, `every`, `some`, `forEach`), mutators (`push`/`pop`, `shift`/`unshift`, `splice`, `concat`, `reverse`, `sort`), and transformation helpers (`join`, `slice`, `flat*`, `fill`, `copyWithin`, `toSorted`, `toReversed`, `toSpliced`, `with`, iterator factories) each living in their own partials so no single file dominates the definition.
- `dotnet build` succeeds with the new layout, so the constructor/prototype reshuffle doesn't break existing projects and existing array tests (e.g., `PrototypeLookupResolvesInheritedMethods`) stay green.
- Function declarations no longer re-instantiate at runtime (evaluation is now a no-op), which fixed the Array.prototype.every subclass failures when a function declaration was overwriting a custom prototype assignment.
- Annex B block-function applicability is now tracked per declaration (not just per name), so nested block functions no longer clobber outer bindings; the `Language_functionCode`/`Language_globalCode` Test262 slices are green again.
- Non-strict `Function.prototype.caller` mirrors Annex B caller metadata instead of poisoning all access, so the `Language ArgumentsObject` caller tests now pass.
- Realm logging is opt-in for Test262 runs (`JSENGINE_TRACE_REALM`), and array helper debug traces have been removed; ToLength now respects abrupt completions so concat with poisoned lengths throws as expected.
- Array.prototype.at now throws a proper TypeError for Symbol indices (propagated via realm-aware ToIntegerOrInfinity), and the Array.prototype.at Test262 slice is green.
- Reflect.construct on Array now falls back to the newTarget realm's `%Array.prototype%` when its `.prototype` is null/undefined, so cross-realm array construction picks up the correct prototype.
- `Array.prototype.concat` now honors @@isConcatSpreadable on Boolean/String wrapper objects without boxing primitives, and Object.prototype.isPrototypeOf walks prototype accessors so new arrays inherit `%Array.prototype%` as expected (`Array_length` slice green).
- `Date.prototype.setYear` now follows the Annex B ordering (captures [[DateValue]] before coercing the argument) and uses the realm time zone; the Date `toLocale*String` helpers are defined as real built-ins, so the `Date_prototype_setYear` and `Date_prototype_toLocaleTimeString` slices pass.
- Date `toLocaleString`/`toLocaleDateString`/`toLocaleTimeString` now delegate to `Intl.DateTimeFormat` with the same default options and option validation, so the `returns-same-results-as-DateTimeFormat` and `throws-same-exceptions-as-DateTimeFormat` Test262 cases pass.
- Date prototype methods now live on `%Date.prototype%` (no per-instance definitions), reuse shared helpers for the set* family, and `toJSON`/`toISOString` match the spec metadata. The `Date_prototype_constructor` and `Date_prototype_getDate` Test262 slices pass again.

## Next Iteration Plan
1. Re-run a wider Test262 sweep (Intl/Date/Temporal, remaining Array_* groups, Language_eval* slices) to list the next failures now that the Annex B hoisting fixes are in place.
2. Keep tightening the partials (shared helpers, comments, tests) so additional array features can slot into the generator model without reintroducing giant files; leave logging opt-in-only for troubleshooting.
