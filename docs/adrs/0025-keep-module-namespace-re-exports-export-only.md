# ADR 0025: Keep module namespace re-exports export-only

## Status

Accepted

## Context

Issue #803 / PR #992 fixed a Test262 `ModuleCode` failure for
`language/module-code/export-star-as-dflt.js`. The failing shape used
`export * as ns from "values.js"` and then observed `typeof ns` inside the
exporting module.

The prior module instantiation path treated the namespace re-export as if it
also created a local lexical binding in the exporting module environment. That
made `ns` visible inside the barrel module and let import binding creation read
the namespace object through a synthetic local binding. ECMAScript module
semantics do not create a local binding for namespace re-exports; they create an
export entry whose value is the source module namespace object.

That distinction matters because the same runtime has local exports,
indirect re-exports, export-star aggregation, and import binding creation in
one owner surface. A re-exported namespace must remain importable by consumers
without becoming a lexical name in the exporting module.

## Decision

`export * as ns from "module"` is modeled as an export-only namespace value.

Module instantiation loads and instantiates the source module, gets its module
namespace object, and stores that direct namespace value in the exporting
module's export table. It does not define `ns` in the exporting module
environment.

Export resolution can now return either a source module binding or a direct
`JsValue`. Import binding creation and live export creation must handle both
forms. Direct namespace values are imported as immutable lexical bindings for
the consumer, while ordinary local and indirect exports continue to use live
environment-backed bindings.

Star export ambiguity checks compare direct namespace values separately from
module/binding resolutions. A direct value must not be folded into a fake local
binding just to reuse the existing live-binding path.

## Consequences

- Future module export work must distinguish local export bindings, indirect
  re-export bindings, and direct export values.
- Do not repair namespace re-export failures by defining the exported namespace
  name in the exporting module environment.
- Focused coverage should prove both sides of the contract: consumers can
  import the namespace export, and code inside the exporting module still sees
  the exported name as unbound.
- The narrow proof for this class should include a local module regression and
  the focused Test262 `ModuleCode` fixture that exercises `export * as`.
