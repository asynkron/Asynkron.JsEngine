# Top-level `await` status

Tracking notes for the current top-level `await` bring-up. Use this as a scratchpad while iterating (similar spirit to `docs/fix-assignment-destructuring-evaluation-order.md`).

## What changed this round
- Async modules now predeclare exports with `AsyncExportBinding` placeholders. The placeholder wraps a realm-scoped `JsPromise` and swaps to the resolved value once set. Export hoisting (`PredeclareExportNames`) creates these promise-backed bindings so imports observe the eventual value instead of `undefined`.
- Module evaluation for async modules runs asynchronously: `EvaluateModuleBodyWithTopLevelAwait` awaits async dependencies, then executes the body on a background task and records the promise on the module entry.
- Await semantics were aligned with the realm Promise prototype. Await wraps non-promise values via `Promise.resolve` (using the realm constructor/prototype) before driving through the shared scheduler.
- Module path normalization regained “keep it relative” behavior for harness loaders. With a module loader present we normalize relative to the referrer directory without stripping the `/language/...` prefix.
- Import evaluation now temporarily detaches the current microtask queue when waiting on an async dependency and prepends the preserved microtasks afterwards. This prevents caller-queued ticks from running while we synchronously block on the imported module.

## Current failing ModuleCode_topLevelAwait tests
Run: `dotnet test tests/Asynkron.JsEngine.Tests.Test262/Asynkron.JsEngine.Tests.Test262.csproj --filter "ModuleCode_topLevelAwait" --logger "console;verbosity=minimal"`

- `language/module-code/top-level-await/await-dynamic-import-resolution.js` (strict)
  - Still throws `Module not found: module-import-resolution_FIXTURE.js`.
  - Normalization now yields `language/module-code/top-level-await/module-import-resolution_FIXTURE.js`, but the loader lookup still fails (likely a mismatch between normalized key and Test262Stream lookup).
- `language/module-code/top-level-await/module-graphs-does-not-hang.js` (strict)
  - Throws `Module not found: module-graphs-grandparent-tla_FIXTURE.js`.
  - Same symptom as above; needs loader path investigation.
- `language/module-code/top-level-await/async-module-does-not-block-sibling-modules.js` (strict)
  - Assertion fails: `check` was `false` (the async sibling ticked earlier than expected). Indicates we’re still draining microtasks from the importing module while awaiting an async dependency; the preserved microtask approach didn’t fully isolate the tick ordering.
- `language/module-code/top-level-await/dfs-invariant.js` (strict)
  - `globalThis.test262` stayed `undefined`; ordering of async parent completion remains off (likely same microtask/drain ordering issue).

## Next steps
1) Module loader resolution: confirm the exact specifier strings passed to `SetModuleLoader` for dynamic import and nested TLA graphs. Add temporary logging or reproduce via a tiny harness to see why `Test262Stream.GetTestFile` misses `module-import-resolution_FIXTURE.js` and `module-graphs-grandparent-tla_FIXTURE.js`.
2) Microtask ordering: prevent caller-queued microtasks from running while a sync import waits on an async dependency. The current detach/restore works only for `EvaluateImport`; promise draining elsewhere (e.g., `AwaitScheduler` loops) still runs the caller’s microtasks. Investigate a dedicated “await while preserving existing queue” path or defer draining until after dependency resolution.
3) Re-run `ModuleCode_topLevelAwait` after fixes and update this file.
