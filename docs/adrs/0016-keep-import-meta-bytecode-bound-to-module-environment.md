# ADR 0016: Keep import.meta bytecode bound to module environment

## Status

Accepted

## Context

Issue #781 / PR #973 fixed the Test262 `Expressions_import_meta_syntax`
failure for `language/expressions/import.meta/syntax/goal-module.js`.
Issue #780 / PR #972 then repaired the remaining identity gap by making the
bytecode runtime and quarantined AST fallback both resolve the module-owned
binding through the active environment chain, including module functions.

The parser already recognized `import.meta` as an `ImportMetaExpression` when
the module syntax goal allowed it, and the module loader already initialized a
stable `Symbol.ImportMeta` binding in the module environment. The failure was
in the IR/expression-program path: `ImportMetaExpression` was not accepted by
the typed support analyzer or lowered by `ExpressionProgramCompiler`, so module
code could not stay on the normal expression bytecode path.

The legacy AST fallback also had a behavior that could synthesize a basic
`import.meta` object when no module binding was present. Carrying that fallback
into expression bytecode would have risked splitting module identity semantics
from the object installed by `JsEngine.EnsureModuleImportMeta`.

## Decision

`import.meta` in expression bytecode is a dedicated load of the existing module
environment binding.

The expression compiler lowers `ImportMetaExpression` to `LoadImportMeta`, and
the expression-program runner resolves `Symbol.ImportMeta` through the active
environment chain. If the binding is missing, the bytecode path reports that
`import.meta` is unavailable outside module evaluation instead of creating a
fresh object.

The legacy AST fallback uses the same module-binding lookup. It must not keep a
local-only environment lookup or synthesize an object, because module functions
inherit the module `import.meta` binding through their environment chain.

Parser syntax-goal checks remain the guard for where `import.meta` is valid.
Expression bytecode support must not broaden script, function-constructor, or
eval acceptance. The runtime load exists only to keep valid module code on the
IR/expression-program path and to preserve the stable module `import.meta`
object identity and URL behavior.

## Consequences

- Future `import.meta` work must inspect parser syntax-goal guards, module
  environment initialization, expression-program lowering, and bytecode runtime
  lookup together.
- Keep bytecode and quarantined AST fallback behavior aligned on the shared
  module-binding resolver; otherwise nested module function contexts can split
  from the module's stable `import.meta` object.
- Do not repair `import.meta` bytecode failures by synthesizing a new
  `import.meta` object in the runner or by falling back to AST evaluation.
- Focused coverage should prove both expression-program lowering and stable
  module binding behavior before widening to the owning Test262
  `Expressions_import_meta_syntax` group.
