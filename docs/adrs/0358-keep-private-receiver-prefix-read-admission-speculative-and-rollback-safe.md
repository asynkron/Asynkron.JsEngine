# ADR 0358: Keep private receiver-prefix read admission speculative and rollback-safe

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-03-com-f16dbb9a61`
/ PR #3342 handled the A51i property-read lane for private receiver-prefix
value reads. Direct private reads such as `receiver.#field` were already
admitted, but receiver-prefix chains such as `receiver.#child.value` and
`receiver.#child[key]` still declined because the simple property-read span
walkers treated every private named hop as a private-neighbor blocker.

The delivery widened only value-read spans. Calls and mutations through the
same receiver-prefix shape, such as `receiver.#child.value()` or
`receiver.#child[key]++`, stayed declined because their call-target,
assignment, update, and delete semantics are owned by other A51 lanes.

The review repair exposed the durable failure mode. The compiler first probed
the whole expression as a private-prefix value-read span, then allowed later
fallback helpers to try their own shapes. Without rolling back emitted
instructions and constant pools after the failed probe, a non-read fallback such
as a computed delete could inherit a partially emitted `box.child` prefix before
emitting the real delete path.

## Decision

Admit private receiver-prefix value reads only through an explicit
whole-expression property-read probe, and make that probe fully speculative.

- Eligibility and compiler span measurement may allow private named hops only
  for the receiver-prefix portion of simple value-read property spans.
- The private-prefix allowance must not silently broaden ordinary mutation,
  delete, call-target, super, or optional-private neighbor admission.
- If a compiler probe appends instructions, literal constants, or string
  constants before discovering the full expression is not the admitted
  value-read shape, it must restore every touched builder before returning
  control to fallback helpers.
- Positive proof for this lane must include public route hits for named and
  computed private receiver-prefix value reads, plus brand-check preservation.
- Nearby no-route proof must pin private receiver-prefix calls and mutations so
  future widening remains explicit.

This keeps private receiver-prefix reads on the production VM without turning a
speculative span walker into a side-effecting partial compiler pass for
unrelated property operations.

## Consequences

- Future A51i work can reuse the private-prefix read measurement path only when
  the emitted shape is still a value read and the whole expression is consumed
  by that read span.
- Compiler helper ordering remains safe: a failed private-prefix probe cannot
  contaminate later computed delete, mutation, call-target, or other fallback
  emission.
- New measured-span probes that can emit before proving the final shape must
  snapshot and roll back all affected builders, not only the unified
  instruction stream.

## Evidence

- PR #3342 merged as squash commit
  `e0f149d41b9fabe5771a03f89cf1ad9418e51601`.
- Delivery repair commit `c39a7b272` fixed the private property read admission
  rollback by restoring partially emitted instructions and constants before the
  computed delete fallback emitted its real path.
- Implementation changed
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
  and
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  to add a scoped `allowPrivateNamedPrefix` path for value-read span
  measurement and compiler emission.
- Focused proof added
  `Evaluate_PrivateReceiverPrefixNamedPropertyRead_AcceptsOwnedPropertyOpcodes`,
  `Evaluate_PrivateReceiverPrefixComputedPropertyRead_AcceptsOwnedPropertyOpcodes`,
  `PrivateReceiverPrefixNamedPropertyRead_UsesUnifiedBytecodeProductionFastPath`,
  `PrivateReceiverPrefixComputedPropertyRead_EvaluatesKeyOnceOnProductionFastPath`,
  and
  `PrivateReceiverPrefixNamedPropertyRead_PreservesBrandCheck`.
- Nearby decline proof kept
  `Evaluate_PrivateReceiverPrefixCall_StillDeclines`,
  `Evaluate_PrivateReceiverPrefixMutation_StillDeclines`, and
  `Evaluate_PrivateReceiverPrefixComputedUpdate_StillDeclines`.
- Build-stage verification recorded
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_NestedNamedReceiverComputedPropertyDeleteCandidate_AcceptsOwnedPropertyOpcodes"`,
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~PrivateReceiverPrefixNamedPropertyRead"`,
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"`,
  and
  `rtk git diff --check 49d481264c153b3b5fba90b74e59a14c83974b47..HEAD`
  passing.
- Learn-stage ADR allocation note: local `rtk faktorial-api adr-next` was not
  present in this worker, so this pass used the runtime allocator endpoint
  `POST /api/adrs/next`, which returned `{"adr_id":358}`.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/unified-bytecode-expansion-contract.md`
- `docs/plans/bytecode-burndown-checklist.md`
- ADR 0355:
  `docs/adrs/0355-admit-computed-prefix-computed-property-mutations-through-owned-spans.md`
