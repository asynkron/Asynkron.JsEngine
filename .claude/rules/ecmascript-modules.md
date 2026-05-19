# ECMAScript Modules

When changing module import/export resolution, keep local bindings, indirect
re-export bindings, and direct namespace export values separate.

## Rules

1. Create module environment bindings only for local declarations and local
   export aliases. A re-export with `FromModule` is not a local lexical name in
   the exporting module.
2. Model `export * as ns from "module"` as an export entry for the source
   module namespace object. Do not define `ns` in the exporting module
   environment.
3. Let export resolution represent both environment-backed bindings and direct
   export values. Import binding creation, live export creation, and star
   ambiguity checks must handle direct values explicitly instead of forcing them
   through synthetic local bindings.
4. Preserve ordinary `export * from` default exclusion separately from explicit
   namespace export forms. Do not use one export-star path as a shortcut for the
   other without focused Test262 proof.
5. Prove namespace re-export fixes with both consumer and exporter visibility:
   importing `{ ns }` from the barrel must work, while `typeof ns` inside the
   barrel remains `undefined`.

## Why

Issue #803 / PR #992 fixed the Test262 `ModuleCode` fixture
`language/module-code/export-star-as-dflt.js`. The old runtime made
`export * as ns from "values.js"` create a local `ns` binding in the exporting
module, which let code inside the barrel observe a name that ECMAScript treats
as export-only. The repair stores the namespace object as a direct export value
and teaches import/export resolution to consume that value without inventing a
source lexical binding.
