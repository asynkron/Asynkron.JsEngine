# ADR 0147: Keep resizable TypedArray smoke fixtures shared

## Status

Accepted

## Context

Issue `autrun-dis4ox67wxio-f9e609a6f1` / PR #1953 continued the recurring
code-reduction work in `TypedArrayResizableTests`.

Before the delivery, `ForLoopResizableTypedArray_JsSmoke` and
`ForOfResizableTypedArray_JsSmoke_Parity` each embedded a large JavaScript raw
string. Most helper code, constructor coverage, grow/shrink setup, BigInt array
handling, and assertions were identical. The intended differences were narrow:

- indexed traversal reads by `iterable[i]`;
- `for...of` traversal reads iterator values;
- fixed-length shrink in the indexed-loop parity test collects `[0, 1]`; and
- fixed-length shrink in the `for...of` parity test is expected to throw.

Duplicating the whole smoke script made future resizable `ArrayBuffer` fixture
changes likely to update one traversal mode but miss the other. At the same
time, merging the tests into one generic loop would have hidden the important
semantic split between indexed access and iterator traversal.

The accepted delivery introduced
`BuildResizableTypedArraySmokeScript(bool useForOf, bool fixedShrinkThrows, int iterations)`
and kept the two public tests as separate call sites with named arguments.
Focused proof ran both affected tests:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --filter "ForLoopResizableTypedArray_JsSmoke|ForOfResizableTypedArray_JsSmoke_Parity"
```

## Decision

Keep the resizable TypedArray JS smoke-test fixture body shared through a small
script builder, while keeping traversal mode and fixed-length shrink behavior
explicit at the call sites.

Future changes to this fixture family should:

1. keep common constructor coverage, grow/shrink setup, BigInt handling, and
   assertion helpers in the shared builder;
2. expose real semantic differences as named builder parameters or separate
   call sites, not as duplicated raw-script blocks;
3. avoid adding conditionals inside the JavaScript fixture for behavior that is
   clearer at the C# call site; and
4. prove both traversal modes together whenever the shared body changes.

Do not collapse the two tests into one parameterized assertion unless the test
names still make the indexed-loop and `for...of` contracts visible in failure
output.

## Consequences

- The parity fixture stays cheaper to maintain because shared RAB/TypedArray
  scenario changes land once.
- The intended spec-observable difference for fixed-length shrink behavior
  remains visible through `fixedShrinkThrows`.
- Focused proof for this boundary is the two-test filter above. A code-size
  signal such as file-level `cloc` is useful for recurring code-reduction
  evidence, but it is not a behavioral proof by itself.
- This ADR owns only the resizable TypedArray smoke fixture shape. It does not
  change runtime TypedArray, `ArrayBuffer.resize`, iterator, or Test262 harness
  behavior.

## Related

- `.claude/rules/recurring-maintenance-child-runs.md`
- `tests/Asynkron.JsEngine.Tests/TypedArrayResizableTests.cs`
