# Language Suite Next Steps

## Current State
- The legacy `StandardLibrary.Array.cs` has been split into focused partials so helper logic (species creation, ReduceLike, iterator plumbing, Array.from/of) lives under `StdLib/Array/StandardLibrary.Array.*.cs`.
- `%Array.prototype%` now comes from the generator-backed `ArrayPrototype` type, with iteration-centric methods (`map`, `filter`, `reduce`, `find*`, `every`, `some`, `forEach`), mutators (`push`/`pop`, `shift`/`unshift`, `splice`, `concat`, `reverse`, `sort`), and transformation helpers (`join`, `slice`, `flat*`, `fill`, `copyWithin`, `toSorted`, `toReversed`, `toSpliced`, `with`, iterator factories) each living in their own partials so no single file dominates the definition.
- `dotnet build` succeeds with the new layout, so the constructor/prototype reshuffle doesn't break existing projects and existing array tests (e.g., `PrototypeLookupResolvesInheritedMethods`) stay green.
- Function declarations no longer re-instantiate at runtime (evaluation is now a no-op), which fixed the Array.prototype.every subclass failures when a function declaration was overwriting a custom prototype assignment.
- Annex B block-function applicability is now tracked per declaration (not just per name), so nested block functions no longer clobber outer bindings; the `Language_functionCode`/`Language_globalCode` Test262 slices are green again.
- Non-strict `Function.prototype.caller` mirrors Annex B caller metadata instead of poisoning all access, so the `Language ArgumentsObject` caller tests now pass.
- Hashbang (`#!`) at the start of Script/eval sources is parsed as a single-line comment, bringing the `Comments_hashbang` slice to green.
- Realm logging is opt-in for Test262 runs (`JSENGINE_TRACE_REALM`), and array helper debug traces have been removed; ToLength now respects abrupt completions so concat with poisoned lengths throws as expected.
- Array.prototype.at now throws a proper TypeError for Symbol indices (propagated via realm-aware ToIntegerOrInfinity), and the Array.prototype.at Test262 slice is green.
- Reflect.construct on Array now falls back to the newTarget realm's `%Array.prototype%` when its `.prototype` is null/undefined, so cross-realm array construction picks up the correct prototype.
- `Array.prototype.concat` now honors @@isConcatSpreadable on Boolean/String wrapper objects without boxing primitives, and Object.prototype.isPrototypeOf walks prototype accessors so new arrays inherit `%Array.prototype%` as expected (`Array_length` slice green).
- `Date.prototype.setYear` now follows the Annex B ordering (captures [[DateValue]] before coercing the argument) and uses the realm time zone; the Date `toLocale*String` helpers are defined as real built-ins, so the `Date_prototype_setYear` and `Date_prototype_toLocaleTimeString` slices pass.
- Date `toLocaleString`/`toLocaleDateString`/`toLocaleTimeString` now delegate to `Intl.DateTimeFormat` with the same default options and option validation, so the `returns-same-results-as-DateTimeFormat` and `throws-same-exceptions-as-DateTimeFormat` Test262 cases pass.
- Date prototype methods now live on `%Date.prototype%` (no per-instance definitions), reuse shared helpers for the set* family, and `toJSON`/`toISOString` match the spec metadata. The `Date_prototype_constructor` and `Date_prototype_getDate` Test262 slices pass again.
- Eval now distinguishes direct vs indirect calls, runs strict eval in an isolated scope, and marks eval-created var bindings as deletable so `delete` behaves per EvalDeclarationInstantiation. Var declarations without initializers no longer resurrect deleted eval bindings.
- Host-provided globals are registered as var-scoped bindings (not lexicals), and direct/indirect eval now walks the caller chain per EvalDeclarationInstantiation (catch parameters are exempt, annex B), so the `Language_evalCode*` slices are green.

## Next Iteration Plan
1. Revisit the remaining language suite failures the user called out earlier (`ComputedPropertyNames_*`, `Destructuring_binding`, `DirectivePrologue`), and repro a focused slice to see what is still red.
2. Trace any regressions back to the recent eval/array refactors and patch the runtime to match the ECMAScript steps (add brief spec breadcrumbs in code where the behavior is non-obvious).
3. Once the above slices are green, refresh failing/*.testsession to keep the rolling todo list accurate.
