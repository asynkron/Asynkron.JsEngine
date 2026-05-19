# ECMAScript Annex B Block Functions

When changing function declaration instantiation, block emission, direct slot
reads, or sloppy/strict scope handling, keep Annex B block-level function
declarations runtime-bound.

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
6. Prove this class with focused coverage: the Test262
   `Name=Language_functionCode` method group or exact failing files, plus local
   strict/sloppy block function tests. Do not use broad harness policy or a full
   Test262 run as a substitute for the semantic proof.

## Why

Issue #794 / PR #991 fixed eight
`annexB/language/function-code/*func-existing-fn-update.js` failures. The bug
was not the Test262 harness: the IR path could skip the runtime declaration
update when direct block function declarations did not get block slots, and the
optimized direct-slot/flat-slot paths could keep reading stale outer values.
Future work in this area must treat Annex B block functions as runtime updates
with multiple backing storage representations, while keeping strict mode block
scoping intact.
