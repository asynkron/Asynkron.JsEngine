# ADR 0228: Keep splice delete-count argument presence shared

## Status

Accepted

## Context

Issue `autrun-ditfscj25yv4-eaf90267d5` / PR #2362 was a bounded recurring
code-reduction slice over `src/Asynkron.JsEngine/StdLib/Array`.

`Array.prototype.splice` and `Array.prototype.toSpliced` repeated the same
`actualDeleteCount` normalization:

- no arguments delete zero elements;
- a present `start` with a missing `deleteCount` deletes through the end;
- an explicitly supplied `deleteCount`, including `undefined`, flows through
  `ToIntegerOrInfinity`;
- positive infinity deletes through the end;
- negative infinity and negative finite values delete zero; and
- finite values are bounded to `0..length - actualStart`.

The surrounding algorithms are intentionally different. `splice` mutates the
receiver, uses `ArraySpeciesCreate` for the removed-elements result, shifts
indexed properties, preserves sparse-array deletion semantics, and writes the
receiver length. `toSpliced` creates a copy, materializes holes as `undefined`
own data properties, applies separate concrete-array length guards, and leaves
the receiver unchanged.

The accepted delivery added `ComputeSpliceDeleteCount(...)` in
`StandardLibrary.Array.Helpers.cs` and routed only the delete-count calculation
from `ArrayPrototype.Mutators.cs` and `ArrayPrototype.Transformations.cs`
through it. It kept the mutating and copying bodies separate.

Issue #2373 / PR #2382 followed through on that boundary after review noticed
the call sites still reread `args.Count` in several nearby branches. The
delivery cached the original argument count once in both `splice` and
`toSpliced`, passed that explicit `argumentCount` into
`ComputeSpliceDeleteCount(...)`, and added focused `splice(undefined)` coverage
to match the existing `toSpliced(undefined)` proof.

Focused evidence recorded by the build and review stages:

```bash
rtk quickdup -path src/Asynkron.JsEngine/StdLib/Array -ext .cs -top 20
rtk roslynator analyze src/Asynkron.JsEngine/Asynkron.JsEngine.csproj --include "src/Asynkron.JsEngine/StdLib/Array/*.cs" -v minimal
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Release --filter "FullyQualifiedName~Splice|FullyQualifiedName~toSpliced"
```

The final focused test run passed 6 tests, Roslynator reported 0 diagnostics,
and the three-file slice moved from 1599 to 1575 C# code lines.

## Decision

Keep splice-family delete-count normalization in one argument-presence-aware
helper.

The durable boundary is:

1. The call sites must capture the original argument count before deriving
   start, insert-count, or delete-count behavior.
2. The helper must receive the original argument list, explicit
   `argumentCount`, `length`, `actualStart`, and the active
   `EvaluationContext?`.
3. `argumentCount == 0`, `argumentCount == 1`, and `argumentCount >= 2` are
   semantic branches. Do not normalize missing arguments into
   `JsValue.Undefined` before this branch.
4. Explicit `undefined` is a supplied value and must flow through
   `ToIntegerOrInfinity(args[1], context)`, producing zero rather than
   `length - actualStart`.
5. Positive infinity, negative infinity, and finite bounds belong in the shared
   helper because they are identical for `splice` and `toSpliced`.
6. Keep method-specific construction, mutation/copy behavior, sparse-hole
   handling, insertion, length guards, and property writes outside the helper
   unless a separate proof shows those steps are also identical.

## Consequences

- Future fixes to splice-family delete-count normalization have one owner.
- Call sites own the observable argument-count snapshot, while the helper owns
  the shared delete-count branch and numeric bounding.
- Argument absence remains observable independently from explicit
  `undefined`, avoiding the common optional-argument regression where a helper
  sees only normalized values.
- `splice` and `toSpliced` can share the count calculation without implying that
  their element movement, result construction, or receiver mutation algorithms
  are interchangeable.
- Code-reduction proof for this seam should include focused `Splice` and
  `toSpliced` tests, `git diff --check`, static analysis, and a code-size or
  duplication signal.

## Related

- Issue `autrun-ditfscj25yv4-eaf90267d5`
- PR #2362
- Issue #2373
- PR #2382
- `.claude/rules/ecmascript-abstract-operations.md`
- `src/Asynkron.JsEngine/StdLib/Array/StandardLibrary.Array.Helpers.cs`
- `src/Asynkron.JsEngine/StdLib/Array/ArrayPrototype.Mutators.cs`
- `src/Asynkron.JsEngine/StdLib/Array/ArrayPrototype.Transformations.cs`
