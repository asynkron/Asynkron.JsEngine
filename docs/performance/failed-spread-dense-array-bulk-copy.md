# Failed: Spread dense-array bulk-copy + pre-sized array literals

Date: 2026-05-30
Issue: autrun-diw47r18vl3k-788d45b06d (Optimizer recurrence)

## Outcome

Below-threshold attempt. A correct, low-risk runtime change produced a
**consistent ~5–7% wall-clock improvement** on the `spread` profile (confirmed
by a rigorous stash/rebuild A/B), but did **not** reach the required 10%+ bar.
The runtime change was **reverted**; only this report is retained.

## Selected profile

The full comparison table (`./benchmark.sh`) selected `spread` as a current
loss with no prior `docs/performance/` entry:

```text
profile        asynkron_ms  jint_ms  delta
spread                895      597  Jint 1.50x faster
```

`spread.js` builds a fresh 11-element array via spread on every loop iteration:

```js
let arr1 = [1, 2, 3, 4, 5];
let results = [];
for (let i = 0; i < 5000; i++) {
    let arr2 = [...arr1, i, ...arr1];   // <- hot
    let obj1 = { a: 1, b: 2 };
    let obj2 = { ...obj1, c: i };
    results.push(arr2.length + obj2.c);
}
results.length;
```

## CPU profile evidence (pre-edit)

```bash
rtk ./tools/profile spread --cpu --calltree-depth 13 --calltree-width 5
```

Inside `ExecuteInstructionLoop`, the `[...arr1, i, ...arr1]` construction was
the dominant cost — **75.9%** of the loop went into incremental `List` growth:

```text
EvaluateExpressionProgram
└─ 75.9% JsArray.Push
   └─ List<JsValue>.AddWithResize
      └─ List<JsValue>.Grow
         └─ List<JsValue>.set_Capacity
            └─ Array.Copy
```

The array starts empty (`CreateArray`) and each `ArraySpread` pushes the source
elements one at a time through `EnumerateSpread`, allocating an iterator-state
object per spread and resizing the backing list several times per array.

## What was tried

Three small, complementary changes (all reverted):

1. **`PackedExpressionOp.CreateArrayWithCapacity(elementCount)`** — array-literal
   compilation seeds `CreateArray` with the static element count so the backing
   `List<JsValue>` is pre-sized (`new JsArray(realm, capacity)`).
2. **`JsArray.TryAppendDenseArraySpread(source)`** — when an `ArraySpread`
   operand is a plain dense `JsArray`, bulk-append its backing list in a single
   pre-sized `AddRange` and bump the length once, instead of element-wise
   `Push`. Guards (`ReferenceEquals`, `_sparseItems is null` on both arrays)
   preserve the exact semantics of `EnumerateSpread`'s array branch and avoid
   the per-spread iterator allocation.
3. Wired (2) into the `ExpressionOpKind.ArraySpread` interpreter case with a
   fall-through to the original `EnumerateSpread` loop for non-array sources.

Engine built clean (0 warnings, 0 errors).

## Why it did not reach 10%

Re-profiling after the change confirmed the optimization worked as intended —
resize churn collapsed from the whole 75.9% down to **7.5%** (`Grow`), and the
iterator allocation disappeared:

```text
EvaluateExpressionProgram
└─ 73.8% JsArray.TryAppendDenseArraySpread
   └─ List<JsValue>.AddRange
      ├─ 66.3% List<JsValue>.CopyTo  -> Array.Copy   (irreducible element copy)
      └─  7.5% List<JsValue>.Grow                       (was the entire cost)
```

But the **wall-clock** barely moved, for two reasons:

1. **The element copy is irreducible.** Spreading `arr1` (5 elements) twice per
   iteration must copy 10 elements per iteration regardless of strategy. For
   such small spreads, `AddRange`/`Array.Copy` does not beat 5 individual
   `List.Add` calls by much — the win is only the removed resizes and iterator
   allocation, not the copy itself.
2. **The benchmark is engine-setup-bound.** `ExecuteInstructionLoop` (where the
   array build lives) is only a fraction of the reported wall-clock; the profile
   top-functions are dominated by `JsEngine.ctor` / `JsEngine.ParseProgram` /
   plan-build per profiler iteration. Even eliminating most of the array-build
   cost moves only a small slice of the total.

## Baseline / Final signal (rigorous A/B)

Method: `git stash` the change, rebuild Release, 6 focused `./benchmark.sh
spread` samples; restore, rebuild, 6 samples. Compare min and median (min is the
least noise-sensitive estimator on a contended dev machine).

```text
Baseline timestamp: 2026-05-30T15:44:00Z
Baseline signal: spread Asynkron (clean) = min 822 ms, median ~900 ms
  samples: 822, 907, 912, 902, 880, 899

Final timestamp: 2026-05-30T15:45:36Z
Final signal: spread Asynkron (optimized) = min 782 ms, median ~837 ms
  samples: 782, 845, 791, 847, 866, 830

Signal delta: -40 ms min (-4.9%), ~-63 ms median (~-7%) — consistent but below
the 10% acceptance threshold. Change reverted.
```

(Jint's row was very noisy across the same window — swinging 555–823 ms — which
is why the per-row `delta` column is unreliable for this profile and a
stash/rebuild A/B on the Asynkron column was used instead.)

## Follow-up guidance for future `spread` runs

- The array-spread construction loop is now **near-optimal** once resize churn
  and the iterator allocation are removed; the floor is the spec-mandated
  element copy. Do not expect a 10% wall-clock win from further tuning the
  `ArraySpread` push path alone.
- Like `simplearithmetic`
  ([failed-simplearithmetic-profiler-sync-evaluate-trials](failed-simplearithmetic-profiler-sync-evaluate-trials.md)),
  `spread` wall-clock is dominated by per-iteration engine setup. A 10% win here
  would require attacking parse/plan/ctor reuse, not the execution loop.
- If the bulk-copy + pre-size change is ever revisited as a general
  allocation-reduction (not a benchmark win), it is correct and low-risk: it
  preserves `EnumerateSpread` array semantics exactly and removes one iterator
  allocation per array spread. It was reverted here only because it missed the
  recurring optimizer's strict 10% benchmark gate.
