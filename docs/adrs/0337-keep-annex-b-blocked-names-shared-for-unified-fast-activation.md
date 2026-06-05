# ADR 0337: Keep Annex B blocked names shared for unified fast activation

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-cbbdaf84ff`
and delivery PR #3233 admitted the A43 descriptor-backed block-scoped function
declaration route into production unified bytecode.

The first part of the delivery made descriptor-backed block function
declarations route by giving the active block binding a flat-slot owner, letting
`PushEnvironment` materialize the strict block environment and letting the VM
execute `DeclareFunction`. That exposed a second activation parity problem:
the accepted production unified-bytecode fast activation path created a
function var environment without installing the same Annex B blocked-name set
used by the IR runner and non-VM sync activation paths.

Without that shared blocked-name setup, sloppy Annex B block-function runtime
updates could incorrectly overwrite an enclosing function binding even when the
function body had an intervening lexical name such as `let f = 123`. The
failure was route-specific: existing non-VM activation already knew how to
block the update, but the production unified activation bridge did not.

## Decision

Production unified-bytecode fast activation must initialize Annex B blocked
names before descriptor-backed block function execution can run.

- Apply the same blocked-name inputs used by existing activation setup:
  body lexical names, parameter names, non-simple catch names, parameter
  expression block-function names, and observable `arguments`.
- Install the set on the function var environment before fast activation hoists
  function-scoped vars, binds parameters, or runs function declaration hoisting.
- Keep the guard sloppy-only and function-declaration-only so ordinary
  fast-activation shapes do not allocate the set when Annex B cannot observe it.
- Require production-route regressions for admitted block-function shapes that
  prove both the route hit and the blocked-name behavior.

## Consequences

- Descriptor-backed block function declarations can route through production
  unified bytecode without weakening Annex B blocked-name semantics.
- Future production fast-activation widening cannot treat slot layout and
  declaration execution as sufficient proof; activation metadata such as Annex B
  blocked names is part of the route contract.
- The focused activation proof pack remains the right internal confidence gate
  for this family, and exact Test262 `FunctionCode` Annex B filters should
  widen confidence when the bug comes from ECMAScript declaration
  instantiation.
- Capturing descriptor-backed block functions still remain declined until the
  VM owns the corresponding closure materialization semantics.

## Evidence

- Delivery PR #3233 merged as squash commit
  `0bd4cc8f159fa79c57cf14b20662d2fe16007008`.
- Delivery commit before squash:
  `94a1f67ea6cf54b86eb236b8292eabedf7414c0e`.
- Implementation changed
  `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs` and
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`.
- Focused proof covered the production route for the `let f = 123` blocked-name
  shape, the synthetic if-branch Annex B update, strict block no-leak behavior,
  the exact Test262 `FunctionCode` plus `block-decl-func-skip-early-err`
  filter, `ActivationSemanticsProofPackTests`, and `rtk git diff --check`.

## Related

- `docs/rules/ecmascript-annex-b-block-functions.md`
- `docs/rules/unified-bytecode-prototypes.md`
- `docs/rules/function-activation-proof-pack.md`
