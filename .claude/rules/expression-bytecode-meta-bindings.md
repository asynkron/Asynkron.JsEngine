# Expression Bytecode Meta Bindings

When adding expression bytecode support for ECMAScript meta-properties, keep
syntax-goal validation, environment binding setup, and bytecode runtime lookup
as separate responsibilities.

## Rules

1. Let the parser and existing syntax-goal options decide where a meta-property
   is valid. Do not broaden script, function-constructor, or eval acceptance as
   a side effect of adding a bytecode load.
2. Lower valid meta-property expressions to dedicated bytecode operations only
   after confirming the runtime binding already has a canonical owner.
3. Runtime bytecode loads must read the existing environment binding that owns
   the semantics. Do not synthesize fallback objects in the expression-program
   runner when the binding is missing.
4. Prove both structure and behavior before widening Test262: lowering should
   show the dedicated op, and runtime tests should prove stable object identity
   or lexical inheritance for the relevant binding.

## Why

Issue #781 / PR #973 fixed `import.meta` module syntax by adding
`LoadImportMeta` to expression bytecode. The durable trap was that
`import.meta` already had a module-environment owner through
`Symbol.ImportMeta`; creating a fresh object in bytecode would have diverged
from module identity and URL behavior. Future meta-property bytecode work should
follow the same split: parser guards validity, environment setup owns the
binding, and bytecode only loads the established binding.
