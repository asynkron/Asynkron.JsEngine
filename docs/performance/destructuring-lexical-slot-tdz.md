# Destructuring lexical slot TDZ marking

Issue `autrun-dirzemwhwz7s-0f70b9a325` selected the `destructuring` profile
from the required `rtk ./benchmark.sh` baseline because it was a current top
Jint-winning row without an existing performance note:

```text
destructuring  asynkron_ms=1859  jint_ms=583  Jint 3.19x faster
```

The required CPU profile was:

```bash
rtk ./tools/profile destructuring --cpu --calltree-depth 40 --calltree-width 40
```

The pre-change call tree attributed most runner time to per-iteration
environment setup:

```text
ExecuteInstructionLoop
└─ HandlePushEnvironment
   └─ ImmutableHashSet.Enumerator<Symbol>.MoveNext
```

`HandlePushEnvironment` already had precomputed slot names to avoid iterating
the slot map during hot scope entry, but TDZ setup still iterated
`LexicalBindings` and probed `SlotMap` on every block or loop environment push.
The destructuring workload creates a lexical block inside a hot `for` loop, so
that repeated set walk dominated the sampled runner cost.

The change adds `PushEnvironmentInstruction.LexicalSlotIndices` and stamps it
for block and loop scopes when slot layout is known. Runtime TDZ marking now
uses direct slot indices and falls back to the old symbol/set path only for
unstamped diagnostic or compatibility instructions.

Post-change focused runs were:

```text
destructuring  asynkron_ms=1357  jint_ms=611  Jint 2.22x faster
destructuring  asynkron_ms=1452  jint_ms=520  Jint 2.79x faster
destructuring  asynkron_ms=1450  jint_ms=545  Jint 2.66x faster
```

Against the recorded 1859 ms baseline, the repeated Asynkron timings improved
by roughly 22-27%.

The post-change CPU profile moved `HandlePushEnvironment` down to 23.14 ms and
shifted the remaining cost to destructuring binding and iterator protocol work:

```text
ExecuteInstructionLoop
├─ HandleBindingVariableDeclaration
│  └─ BindArrayPatternProgram
│     └─ TryGetIteratorForDestructuring
└─ HandlePushEnvironment
   ├─ JsEnvironment.SetSlotMap
   └─ JsEnvironmentPool.Rent
```

Focused verification:

```bash
rtk dotnet build
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~StatementInstructionStorageDiagnosticsTests.PushEnvironment_DiagnosticsEncoding_RoundTripsOperandPayload|FullyQualifiedName~DestructuringTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
```
