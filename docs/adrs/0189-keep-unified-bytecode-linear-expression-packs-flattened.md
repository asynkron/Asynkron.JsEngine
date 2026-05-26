# ADR 0189: Keep unified bytecode linear expression packs flattened

## Status

Accepted

## Context

Issue #2158 / PR #2162 expanded the unified bytecode prototype past the first
direct-return and single-declaration slices. The accepted body shape is still a
sync-only, linear function body, but it can now walk zero or more non-awaited
`SimpleVariableDeclarationInstruction`s through `Next` and finish at one
non-awaited `ReturnInstruction`.

The representative target is:

```js
function f(x, y) {
  var a = x + y;
  var b = a * 2;
  return b;
}
```

The important choice was how to bridge statement IR and existing
`ExpressionProgram` payloads. A broad bridge such as an `EvalExpressionProgram`
opcode or a VM callback into the existing expression interpreter would have
made the prototype appear to support more shapes than it actually owned. The
accepted slice instead copied only supported expression operations into unified
bytecode:

```text
LoadSlot x
LoadSlot y
Binary Add
StoreSlot a
LoadSlot a
LoadLiteral 2
Binary Multiply
StoreSlot b
LoadSlot b
Return
```

Literal constants are stored on `UnifiedBytecodeProgram` and loaded with
`LoadLiteral`. The VM executes only the emitted unified instructions. Binary
operator support expanded to `+`, `-`, `*`, `/`, and `%`, with modulo using
`JsOps.MathMod`, but the prototype still treats these as numeric VM operations,
not as proof of full JavaScript operator coercion.

## Decision

Keep unified bytecode linear expression packs flattened into owned unified
instructions and program-owned literal constants.

- Follow `ExecutionPlan.Instructions` linearly through supported declaration
  instructions and stop at one supported return instruction.
- Copy only supported `ExpressionProgram` operations into unified bytecode:
  identifier loads become `LoadSlot`, literals become `LoadLiteral`, and the
  currently proven numeric binary operators become `Binary`.
- Store literal constants on `UnifiedBytecodeProgram`; do not retain or execute
  the source `ExpressionProgram` from inside the unified VM.
- Reject any unsupported statement shape, expression op, awaited path, invalid
  `Next` target, assignment, expression statement, branch, environment
  transition, async function, or generator function during compilation.
- Treat `+`, `-`, `*`, `/`, and `%` as a narrow numeric prototype surface until
  a later issue proves JavaScript coercion parity or routes through a semantic
  operator owner deliberately.

## Consequences

- The unified VM stays fallback-free. Unsupported expression coverage remains
  visible as a compile-time decline instead of being masked by
  `ExpressionProgram` execution.
- Literal storage now belongs to the unified program for this prototype path,
  which gives later slices a clear constant-table owner to extend or replace.
- Future shape expansion must add positive opcode/execution tests and nearby
  negative decline tests for the exact new surface it accepts.
- Production execution still stays on the existing statement IR plus
  expression-bytecode runtime until a separate routing issue proves coverage,
  parity, unsupported-shape accounting, and performance evidence.

## Related

- Issue #2158
- PR #2162
- ADR 0181: `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`
- ADR 0186: `docs/adrs/0186-keep-unified-bytecode-function-kind-eligibility-explicit.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodePrototypeTests.cs`
