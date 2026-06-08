# ADR 0375: Admit static-block super property routes through descriptor-owned this writes

## Status

Accepted.

## Context

Faktorial issue
`planitem-gh3495-shared-context-e5c-script-and-static-block-runner-retirement-reti-e9fd42d64f`
and delivery PR #3516 targeted the next narrow E5c static-block runner-retirement
slice: derived static blocks that use non-dynamic `super` property reads and
assign the result through `this`.

The existing static-block bridge already attempted production unified bytecode
before falling back to `ExecutionPlanRunner.RunScript`, and ADR 0364 kept the
remaining fallback classified by the production decline. This slice needed a
narrow admission without blurring three boundaries:

- ordinary script fallback still belongs to `RunScriptViaClassifiedIrFallback`;
- runtime-source direct eval inside static blocks remains explicit B24h/B36
  declined residue;
- static-block-specific `this` and `super` semantics must be admitted only when
  the static initialization environment and VM opcodes already own them.

The build also exposed a quality-gate repair: adding named `super` property read
support in the compiler's complex-argument walker introduced a retained decline
template that had to be recorded in the expansion contract inventory.

## Decision

Admit the derived static-block `super` property family through the existing
production unified-bytecode static-block route, not through a new runner or VM
fallback.

- `ClassDefinitionExtensions.ExecuteStaticBlock` may pass
  `AllowsThisPropertyWrites: true` in its production activation descriptor
  because the static initialization environment provides the constructor as the
  VM `this` binding for the accepted static-block section.
- Keep that flag entrypoint-owned. Do not treat it as broad permission for
  ordinary functions, scripts, class declarations, or resumable bodies to admit
  new `LoadThis`-based property writes.
- Admit named `super` property reads only through the validated expression
  regions that already preserve operand order and compile to
  `GetNamedSuperProperty`. Private, optional, or nullish-short-circuit named
  `super` property reads remain unsupported in that complex-argument region.
- Keep the eligibility and compiler walkers mirrored: a source shape that
  production eligibility accepts must also compile without falling through to a
  generic unsupported-expression diagnostic.
- When the compiler adds or retains a non-empty decline reason for these
  guarded neighbors, record the exact template in
  `docs/unified-bytecode-expansion-contract.md` in the same delivery.

## Consequences

- B24h computed class expressions and B36 direct class declarations can now prove
  the derived static-block `super` property family with
  `unified-bytecode-production-fast-path static-block` and with
  `classified-static-block-ir-fallback` forbidden.
- The remaining static-block fallback stays classified E5 residue for
  non-production static-block plans; ordinary script fallback and direct-eval
  terminal dynamic residue remain separate owner rows.
- Future static-block widening must move descriptor flags, plan-shape
  eligibility, compiler emission, manifest rows, and contract diagnostics
  together. A one-surface update is incomplete even when the JavaScript shape
  looks small.

## Evidence

- ADR allocation note: `rtk faktorial-api adr-next` was unavailable in this
  runtime (`[rtk: No such file or directory (os error 2)]`), so this learn pass
  used the Faktorial runtime HTTP allocator endpoint
  `POST /api/adrs/next`, which returned `{"adr_id":375}`. The prefix `0375`
  was checked free before writing.
- Delivery PR #3516 merged as commit
  `6bfedf94295c17cf94af87e78e13af5477b7c73c`.
- Build-stage commit `52ead303e` admitted the static-block `super` property
  route. Re-entry repair commit `5e1991d81` recorded the retained compiler
  decline template `Unsupported named super property read in complex call
  argument.` in the expansion contract.
- Build-stage verification recorded:
  - focused B24h/B36 static-block `super` property tests passed;
  - focused `BytecodeProofManifestTests` plus the static-block fallback source
    gate passed;
  - the one-test compiler decline-template contract repair passed;
  - `rtk git diff --check` passed.

## Related

- ADR 0364:
  `docs/adrs/0364-keep-class-static-block-ir-fallback-classified-by-production-decline.md`
- ADR 0322:
  `docs/adrs/0322-keep-unified-bytecode-compiler-decline-inventory-source-guarded.md`
- `docs/rules/unified-bytecode-prototypes.md`
- `docs/plans/bytecode-proof-manifest.json`
- `docs/unified-bytecode-expansion-contract.md`
- `src/Asynkron.JsEngine/Ast/ClassDefinitionExtensions.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
