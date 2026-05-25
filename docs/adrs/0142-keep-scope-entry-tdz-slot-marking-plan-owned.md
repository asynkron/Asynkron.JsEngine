# ADR 0142: Keep scope-entry TDZ slot marking plan-owned

## Status

Accepted

## Context

Issue `autrun-dirzemwhwz7s-0f70b9a325` / PR #1915 selected the
`destructuring` benchmark from the optimizer baseline because it was a current
top Asynkron-vs-Jint loss:

```text
destructuring  asynkron_ms=1859  jint_ms=583  Jint 3.19x faster
```

The focused CPU profile showed the hot owner under block/loop scope entry:

```text
ExecuteInstructionLoop
└─ HandlePushEnvironment
   └─ ImmutableHashSet.Enumerator<Symbol>.MoveNext
```

`PushEnvironmentInstruction` already carried precomputed slot names, but TDZ
setup still iterated `LexicalBindings` and probed the scope `SlotMap` on every
environment push. The destructuring benchmark repeatedly enters a lexical block
inside a hot loop, so the set walk and map lookup became runtime overhead in a
shape whose slot layout was already known at lowering/stamping time.

The accepted delivery added `PushEnvironmentInstruction.LexicalSlotIndices`,
stamped block and loop scope instructions with direct lexical slot indices when
the slot map is available, and kept the older symbol/set path only as a fallback
for unstamped diagnostic or compatibility instructions. Repeated focused
benchmark runs improved Asynkron `destructuring` time from the recorded
1859 ms baseline to 1357-1452 ms, and the post-change CPU profile moved
`HandlePushEnvironment` down to 23.14 ms.

## Decision

Keep scope-entry lexical TDZ slot marking owned by the lowered execution plan
metadata.

When an emitter or plan-stamping pass knows the slot map for a block, loop, or
future lexical environment push, it should precompute direct lexical slot
indices and store them on the `PushEnvironmentInstruction` payload. The runtime
scope-entry handler should consume those indices directly and mark TDZ state by
slot index.

The symbol-based `LexicalBindings` plus `SlotMap` lookup path remains a
compatibility and diagnostics fallback for instructions that cannot yet be
stamped with direct indices. It should not be the hot path for ordinary lowered
block or loop scopes once slot layout is available.

Do not reintroduce per-push lexical set iteration or slot-map probing in
`ExecutionPlanRunner` to solve scope-entry TDZ behavior for known layouts. If a
new lexical scope shape needs TDZ marking, extend the emitter/stamping side so
the plan carries the runtime-ready slot-index payload, then prove the fallback
still covers unstamped compatibility records.

## Consequences

- Runtime scope entry stays a metadata-consumption path instead of repeating
  lexical binding analysis on every environment push.
- Block and loop scope emitters remain responsible for converting
  symbol-owned lexical declarations into runtime-ready slot-index payloads once
  slot assignment is known.
- Diagnostics and compatibility encode/decode surfaces must preserve
  `PushEnvironmentInstruction` lexical slot metadata when they claim
  record-level parity.
- Future performance work on lexical scope entry should prove the hot owner with
  the selected profile, then keep the fix on the plan/emitter/stamping boundary
  unless the slot layout is genuinely unavailable.
- Remaining destructuring cost shifted to binding and iterator protocol work;
  those are separate optimization owners and should not be conflated with
  environment TDZ setup.

## Related

- `docs/performance/destructuring-lexical-slot-tdz.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `.claude/rules/statement-bytecode-packing.md`
