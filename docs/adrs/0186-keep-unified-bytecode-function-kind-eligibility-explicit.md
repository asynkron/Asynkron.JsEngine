# ADR 0186: Keep unified bytecode function-kind eligibility explicit

## Status

Accepted

## Context

Issue #2139 / PR #2144 expanded the unified bytecode prototype from the first
sync return-expression slice into a tiny linear statement sequence:

```text
SimpleVariableDeclarationInstruction -> ReturnInstruction
```

The accepted shape can lower:

```js
function addViaLocal(a, b) {
  var c = a + b;
  return c;
}
```

into:

```text
LoadSlot a
LoadSlot b
Binary Add
StoreSlot c
LoadSlot c
Return
```

The compiler still uses the already-lowered `ExecutionPlan` as its source of
truth and remains all-or-nothing. Review feedback on the delivery identified a
separate eligibility dimension: async and generator functions can have simple
body instruction shapes, but their observable execution contract is not the
same as a sync function returning a raw `JsValue`.

Async functions return promises and must preserve await/resume behavior.
Generators return iterators and suspend/resume through generator completion
state. A linear non-awaited body shape is therefore not enough proof that the
current unified VM can execute the function semantics.

## Decision

Keep function kind as an explicit unified-bytecode compiler eligibility input.

For the current prototype, `UnifiedBytecodeCompiler.TryCompile` must reject
async or generator functions before walking otherwise-compatible instruction
shapes. Accepted shapes are sync, non-generator function plans only.

Do not infer sync-only eligibility from `ExecutionPlan` instruction shape,
absence of `AwaitedProgram`, or opcode support alone. Future async or generator
unified-bytecode work must first define the VM/runtime semantics it can execute,
then relax the function-kind guard with focused tests for that executable
contract.

## Consequences

- Shape expansion stays safe: a body that looks like the sync prototype still
  declines when the surrounding function kind requires promise, iterator, or
  suspension semantics.
- Prototype tests need to pass function-kind metadata from the parsed function
  declaration into the compiler instead of testing plans as context-free bodies.
- Each future shape slice should include nearby function-kind negative tests
  when the syntax could also appear in async or generator functions.
- The unified VM remains fallback-free and does not become responsible for
  emulating `AsyncFunctionInvoker`, `SyncGeneratorInvoker`, or
  `AsyncGeneratorInvoker` behavior by accident.
- Runtime routing still requires a separate proof pack before production
  execution can use the unified VM.

## Related

- Issue #2139
- PR #2144
- ADR 0181: `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodePrototypeTests.cs`
