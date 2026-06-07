# ECMAScript Direct Eval Declaration Instantiation

When changing `EvalHostFunction`, direct eval parsing, declaration
instantiation, or scope-collision checks, preserve strict/sloppy eval behavior
and keep `arguments` handling path-specific.

## Rules

1. Keep sloppy direct eval `var arguments;` in ordinary function code allowed.
   Do not route this shape through generic parameter-binding or lexical
   collision rejection just because the caller has an `arguments` binding.
2. Keep strict eval and strict parser binding validation for `arguments`
   separate. A sloppy direct-eval exception must not weaken strict-mode
   `arguments` rejection.
3. Keep non-simple-parameter restrictions as a dedicated guard. If a future
   issue involves parameter-default or non-simple-parameter direct eval, update
   that guard and prove the parameter shape directly instead of widening
   generic lexical-collision behavior.
4. For the special `arguments` binding in eval declaration checks, prefer
   ordinal symbol-name comparison over reference identity. The semantic
   question is the binding name, not whether the symbol instance is interned.
5. Prove changes with both a focused internal regression and the owning
   Test262 filter or fixture. For issue #1834 / PR #1853, the internal guard is
   `TestVariableDeclarations.DirectEvalVarArgumentsInSloppyFunction_ShouldBeAllowed`
   and the external fixture is
   `language/statements/variable/12.2.1-11.js` under `Statements_variable`.
6. Keep arrow parameter environments out of ordinary-function-only
   `arguments` collision rules. Arrows do not create their own arguments
   object, but they can still have an own parameter binding named `arguments`.
   Direct-eval fixes in this area must distinguish those two facts: reject
   sloppy direct-eval `var arguments` only when the parameter environment
   actually has an own `arguments` binding, and prove both the inherited-arrow
   case that must stay allowed and the explicit-parameter case that must throw.
7. For strict direct eval, reuse the already-created strict direct-eval lexical
   environment only when declaration collection proves the eval program has no
   top-level var-declared names, no top-level lexical declarations, and no
   var-scoped function declarations. Declaration-bearing strict direct eval
   must keep the child `eval` environment path, and sloppy direct eval plus
   indirect eval must stay on their existing paths. The reused environment is
   the fresh strict direct-eval lexical environment for this eval call, not the
   caller environment, unless the narrower caller-strict no-environment rule
   below applies. Mark whichever environment executes declaration
   instantiation as the eval declaration environment before instantiation.
   Prove this with a declaration-leak negative test and the focused
   activation/class-element eval proof when environment depth changes.
8. For declaration-free strict direct eval in an already-strict caller, it is
   valid to skip creating any eval environment and execute directly in the
   caller environment only after the normal parse/cache lookup, strict-reserved
   binding validation, control-flow validation, `super` / `new.target`
   validation, private-name validation, and declaration collection have run.
   Require `hasStrictCaller`; strict source executed from a sloppy caller stays
   on rule 7's fresh strict direct-eval lexical environment path. Do not mark
   the caller environment as an eval declaration environment on the no-env
   path, because there are no declarations to instantiate and the optimization
   must not mutate caller activation state. Prove this with repeated current
   activation binding observations plus the focused activation/class-element
   eval proof.
9. When extending direct-eval program cache follow-through, cache only
   immutable program-shaped static analysis beside non-template cached
   `ProgramNode` values. Allowed facts include module/import-meta presence,
   `EvalValidationFlags`, declaration/name collections, var-function
   declarations, strict-reserved binding presence, and declaration-free
   classification when the cache key includes the relevant strictness input.
   Keep private-name validation outcomes, `super` / `new.target` eligibility,
   class-field-initializer state, declaration-instantiation effects,
   execution results, and caller environment decisions outside the cache.
   Eval sources that may contain template literals remain governed by
   `docs/rules/ecmascript-template-object-cache.md` and must not reuse a
   cached `ProgramNode` without a separately proven eval-instantiation
   identity.
10. When widening production unified-bytecode routing around direct eval,
    keep declaration-bearing direct eval out of the VM route until declaration
    instantiation is VM-owned. Literal eval sources containing top-level `var`,
    `let`, `const`, function, or class declarations must decline before VM
    execution, and runtime-source direct eval must remain on the IR path.
    Preserve already-admitted declaration-free direct eval shapes separately:
    do not turn the guard into a blanket direct-eval decline. In resumable
    unified-bytecode activation, keep the declaration-free optimization
    route-family scoped: sync generators and async functions may admit the
    literal declaration-free/no-`arguments` subset, while async generators must
    stay on their conservative direct-eval decline until their settlement path
    has dedicated proof. Prove the boundary with both eligibility no-route tests
    for declaration-bearing literals and invocation tests that strict direct
    eval declarations stay isolated from the caller while logging no
    `unified-bytecode-production-fast-path` hit, plus a declined-neighbor
    async-generator settlement test when this surface is touched.
11. When rebaselining bytecode inventory or proof-manifest rows around direct
    eval, keep admitted and residue sublanes explicit under the same open
    umbrella row when both still exist. Use admitted proof rows for
    declaration-free, single-literal direct eval routes, open decline rows for
    declaration-bearing or runtime-source direct eval, and hard-quarantined rows
    for terminal multi-arg/spread residue. Do not relabel D1/D2 terminal
    residue as ordinary A2 work, and do not count the admitted declaration-free
    lane as open residue.

## Why

Issue #1834 / PR #1853 fixed a Test262 variable-statement failure where
sloppy direct eval rejected `eval("var arguments;")` inside an ordinary
function. The bug survived an initial parameter-binding fix because the generic
lexical-collision guard still treated the caller's `arguments` slot as a
collision. Future work in this area should start at direct eval declaration
instantiation and prove the strict/sloppy and parameter-environment split
explicitly, rather than changing parser strictness or generic declaration
execution.

Issue #1918 / PR #1920 revisited the same surface for direct eval in arrow
parameter environments named `arguments`. The exact reported `EvalCode_direct`
rows and the broader method group were already green on current main, so the
durable lesson is to preserve the existing ordinary-function versus arrow
environment split and to stop on current focused proof instead of broadening
direct-eval collision logic from a stale Test262 batch report.

Issue #2002 / PR #2010 turned that stale green closeout into a real edge-case:
the over-broad arrow exception allowed
`(p = eval("var arguments = 'param'"), arguments) => {}` even though the arrow's
parameter environment itself declared `arguments`. The repair kept arrow
inherited-arguments behavior allowed, but narrowed the non-simple-parameter
guard to the actual own `arguments` binding. Future changes on this surface
must not simplify this back to either "all arrow parameter environments are
exempt" or "all arrow parameter environments reject"; the observable split is
own parameter binding versus inherited outer arguments object.

Issue #2228 / PR #2241 optimized `activation-evalscope-lite` by removing one
empty environment allocation from declaration-free strict direct eval. The win
was safe only because strict direct eval had already created a fresh lexical
environment for the eval call and the parsed program had no declarations to
instantiate. Future performance work must not generalize that slice to
declaration-bearing eval programs: those still need the child eval environment
to isolate strict eval declarations from the caller while preserving direct
eval observability for `arguments`, `new.target`, `super`, private-name scopes,
and class-element contexts.

Issue #2257 / PR #2262 removed the next empty environment only for the narrower
case where the caller was already strict and the eval program was
declaration-free. The durable lesson is that no-environment direct eval is not
the same as generic strict eval: it must be caller-strict, declaration-free, and
post-validation, and it must not mark the caller environment as an eval
declaration environment.

Issue #2595 / PR #2600 removed repeated non-template direct-eval static scans
on cache hits by storing immutable program-shaped analysis next to the parsed
program. The durable lesson is the same caller-context boundary in a smaller
form: analysis facts may follow the parsed program only when they are stable for
the eval cache key and cannot mutate across calls; caller-context validation and
declaration instantiation still run per invocation.

Faktorial issue
`planitem-planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndo-0db19db6ec`
/ PR #3311 hardened the production-route boundary without adding a VM-owned
declaration-instantiation implementation. The useful lesson is that this is a
pre-VM route-selection boundary, not a semantics shortcut: declaration-bearing
direct eval has to decline before production VM execution, while existing
declaration-free direct eval route hits remain valid and should not be
regressed by an over-broad guard.

Faktorial issue
`planitem-planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndo-29a0fd043c`
/ PR #3316 then widened the declaration-free direct-eval resumable route only
for sync generators and async functions. The review repair kept async
generators declined because their promise-settlement and classified fallback
contract is a separate route-family proof problem. Future direct-eval widening
must preserve that per-invoker flag split instead of inferring async-generator
safety from a sync-generator or async-function proof.

Faktorial issue
`planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-7320768cfe`
/ PR #3383 rebaselined the A2 direct-eval proof inventory without runtime code
changes. The useful lesson is taxonomy hygiene: A2 is mixed, so the proof
manifest must distinguish admitted declaration-free literal eval and bounded
caller-activation or implicit-`arguments` reads from declaration-bearing eval,
runtime-source eval, and terminal multi-arg/spread D1/D2 residue. Future
inventory passes should preserve that split instead of flattening the whole
row into either "admitted" or "residue".

Related ADRs:

- `docs/adrs/0185-keep-direct-eval-program-cache-strictness-and-caller-context-owned.md`
- `docs/adrs/0132-keep-direct-eval-var-arguments-collision-checks-narrow.md`
- `docs/adrs/0206-keep-strict-direct-eval-declaration-free-environment-reuse.md`
- `docs/adrs/0213-keep-strict-direct-eval-no-environment-fast-path-caller-strict.md`
- `docs/adrs/0352-keep-resumable-direct-eval-admission-route-family-scoped.md`
