# ADR 0091: Keep module namespace construction resolution-lazy

## Status

Accepted

## Context

Issue #1376 / PR #1381 fixed Test262 `ModuleCode` failures for
`language/module-code/eval-self-once.js` and
`language/module-code/instn-once.js`.

The failing shape imported and re-exported the current module several ways,
including namespace forms such as `import * as ns from "./self-once.js"` and
`export * as ns from "./self-once.js"`. Module namespace construction used the
exported-name list, then eagerly called `ResolveExport` for every name before
the namespace object existed. For a self namespace re-export, resolving the
namespace export asked for the current module namespace again and recursively
entered namespace construction until the stack overflowed.

This is adjacent to ADR 0025's export-only namespace re-export rule and ADR
0027's self-import cycle rule, but the durable decision here is narrower: the
act of creating the namespace object must not recursively resolve every export
binding up front.

## Decision

Construct module namespace objects from the sorted exported-name list and defer
binding resolution to actual namespace observation.

`GetModuleNamespace` may ensure the module is instantiated, collect exported
names, filter internal getter/setter/symbol bridge names, and create the
`ModuleNamespace`. It must not eagerly resolve each exported name as a
precondition for object creation. The namespace lookup callback remains the
place that calls `ResolveExport`, reads direct namespace values, or reads live
environment-backed bindings.

Self-import and self namespace re-export cycles therefore reuse the current
module entry and can finish namespace object construction without recursively
demanding the same namespace before it is cached.

## Consequences

- Future module namespace work must keep namespace object creation separate
  from export binding reads.
- Do not repair self-import or self-re-export recursion by adding another
  evaluation guard that hides module identity problems; fix the construction
  and lookup boundary instead.
- Focused coverage for this class should include a local regression that
  imports and re-exports the same module through empty, default, star, and
  namespace forms and asserts the module evaluates once.
- Review should also keep a nearby ambiguity proof, such as Test262
  `language/module-code/instn-star-ambiguous.js`, because namespace key
  construction and lazy lookup must not regress star-export ambiguity behavior.
- This ADR complements ADR 0025, ADR 0027, and ADR 0080.
