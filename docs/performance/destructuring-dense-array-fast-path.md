# Destructuring dense array fast path

## Profile

Automation run `autrun-disb2md7n23s-0c3cde8865` selected `destructuring`
from `rtk ./benchmark.sh` because it was one of the largest current
Asynkron-vs-Jint losses:

```text
destructuring                  1388      483  Jint 2.87x faster
```

Repeated selected-profile baseline timings:

```text
rtk ./tools/compare-jint-profiles --no-build destructuring
destructuring                  1454      537  Jint 2.71x faster
destructuring                  1442      484  Jint 2.98x faster
destructuring                  1520      506  Jint 3.00x faster
```

Asynkron average: `1472ms`.

## Finding

The required CPU profile command was:

```text
rtk ./tools/profile destructuring --cpu --calltree-depth 40 --calltree-width 40
```

The baseline call tree put most of the selected benchmark inside binding
declarations and array binding:

```text
ExecuteInstructionLoop                                         323.41 ms
HandleBindingVariableDeclaration                               165.30 ms
ApplyBindingTargetProgram                                      150.95 ms
BindArrayPatternProgram                                        129.04 ms
TryGetIteratorForDestructuring                                  61.98 ms
ArrayPatternIterator.Next                                       46.08 ms
```

The benchmark repeatedly destructures fresh dense arrays with the default array
iterator:

```js
const arr = [i, i + 1, i + 2];
const [x, y, z] = arr;
```

The generic path still has to honor custom iterators, holes, defaults, rest
elements, nested targets, and abrupt iterator close behavior. For this dense
default-iterator shape, that work was avoidable.

## Change

The kept slice adds two narrow fast paths:

- `BindArrayPatternProgram` binds dense `JsArray` values directly when the
  binding is identifier-only, has no defaults/rest, every consumed index is an
  own element, and the array still resolves to the native `values` iterator.
- `ArrayPatternIterator` and the IR destructuring handler read standard
  `IteratorResultObject` values directly and return non-escaping pooled result
  objects.

The guard intentionally falls back for custom `Array.prototype[Symbol.iterator]`,
holes, indexed descriptors, defaults, rest, nested binding targets, and generator
contexts.

## Final Signal

Repeated selected-profile final timings:

```text
rtk ./tools/compare-jint-profiles destructuring
destructuring                   959      597  Jint 1.61x faster

rtk ./tools/compare-jint-profiles --no-build destructuring
destructuring                   982      550  Jint 1.79x faster
destructuring                   990      546  Jint 1.81x faster
```

Asynkron average: `977ms`, a `33.6%` improvement from the `1472ms` baseline
average.

Final CPU profile:

```text
ExecuteInstructionLoop                                         176.63 ms
HandleBindingVariableDeclaration                                38.93 ms
ApplyBindingTargetProgram                                       18.13 ms
BindArrayPatternProgram                                          6.57 ms
TryBindDenseArrayPatternProgram                                  4.92 ms
```

## Verification

Focused semantic proof:

```text
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~DestructuringTests|FullyQualifiedName~DestructuringIteratorTests|FullyQualifiedName~AstFreeExecutionAssertionTests.AssertNoAstEvaluation_Enabled_AllowsSimpleArrayDestructuringInitialization" -- xUnit.MaxParallelThreads=1 -timeout 20000
ok dotnet test: 92 tests passed
```

The focused pack includes a custom `Array.prototype[Symbol.iterator]` regression
that proves the dense-array path does not bypass observable custom iterator
semantics.
