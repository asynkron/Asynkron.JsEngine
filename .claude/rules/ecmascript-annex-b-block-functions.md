# ECMAScript Annex B Block Functions

When changing parser block-declaration validation, function declaration
instantiation, block emission, direct slot reads, or sloppy/strict scope
handling, keep Annex B block-level function declarations runtime-bound and keep
parse-phase redeclaration checks scoped to the correct syntactic container.

## Rules

1. Do not model sloppy block-level function declarations as ordinary eager
   function-scope hoists. The outer binding must keep its prior value until the
   containing block, branch, or switch case executes.
2. Direct block function declarations need a real block-environment slot even
   when the block has no other top-level lexical names. The block slot is the
   value copied outward during Annex B runtime evaluation.
3. Keep TDZ handling separate from slot allocation. Function declaration names
   may need slots, but they should not be added to the block's uninitialized
   lexical bindings.
4. When Annex B updates the enclosing binding, update every read surface that
   can observe the value: the var environment binding, intermediate body or
   block slots, and any flat-slot handle backed by that binding.
5. Preserve strict-mode and blocked-name behavior. Strict functions and Annex B
   cases blocked by intervening lexical declarations must not receive the sloppy
   outer-binding update.
6. Parser-time block redeclaration checks must distinguish ordinary blocks,
   function bodies, and class static blocks. Ordinary non-function blocks treat
   direct block function declarations as lexical names for the
   LexicallyDeclaredNames-vs-VarDeclaredNames early error. Function bodies skip
   that block-level check, and class static blocks keep `let`/`const`/`class`
   conflicts with `var` while allowing direct `function f() {}` plus `var f`.
7. Var-declared-name collection for parser early errors may recurse through
   same-function statement bodies such as nested blocks, branches, loops, switch
   cases, and try/catch/finally, but must not enter nested function bodies or
   class bodies.
8. Do not move static parse-phase redeclaration failures into runtime hoisting,
   IR lowering, Test262 harness policy, or generated Test262 files.
9. Keep async/generator switch and catch declarations eval-aware. Ordinary
   sloppy non-eval execution must not leak `async function`, `async function*`,
   or `function*` declarations into the enclosing var binding through Annex B.
   Eval declaration environments still preserve eval var-binding update
   semantics, so do not suppress solely from declaration kind.
10. Prove this class with focused coverage: the Test262
   `Name=Language_functionCode` method group or exact failing files, plus local
   strict/sloppy block function tests. For parse-phase block redeclaration
   issues, include local block-scope parser regressions plus the exact
   `Name=BlockScope_syntax_redeclaration` method group. For switch
   async/generator declaration scoping, include `Name=Statements_switch`, local
   sloppy switch async/generator tests, and eval switch var-update tests. Do not
   use broad harness policy or a full Test262 run as a substitute for the
   semantic proof.

## Why

Issue #794 / PR #991 fixed eight
`annexB/language/function-code/*func-existing-fn-update.js` failures. The bug
was not the Test262 harness: the IR path could skip the runtime declaration
update when direct block function declarations did not get block slots, and the
optimized direct-slot/flat-slot paths could keep reading stale outer values.
Future work in this area must treat Annex B block functions as runtime updates
with multiple backing storage representations, while keeping strict mode block
scoping intact.

Issue #1022 / PR #1095 fixed the
`BlockScope_syntax_redeclaration` negative parse-phase crash by adding a
parser-owned LexicallyDeclaredNames-vs-VarDeclaredNames check for block
statement lists. The review bounce showed why future work must keep the
syntactic boundary precise: class static blocks still reject lexical
declarations that conflict with `var`, but direct function declarations inside a
static block are not part of that lexical-name side for `function f() {}` plus
`var f`.

Issue #1069 / PR #1241 fixed focused `Statements_switch` failures where
sloppy switch clauses containing `async function`, `async function*`, or
`function*` declarations incorrectly exposed the declaration name outside the
switch. The quality-gate repair showed the durable boundary: ordinary non-eval
switch/catch execution suppresses the Annex B outer var update for these
declarations, but eval declaration environments must keep eval var-binding
update semantics. Related ADR:
`docs/adrs/0075-keep-switch-async-generator-declarations-eval-aware.md`.
