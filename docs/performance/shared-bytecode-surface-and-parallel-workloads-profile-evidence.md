# Shared bytecode surface and parallel workloads profile evidence (#4232277e24)

Date: 2026-05-28
Issue: planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-4232277e24

## Commands run

```bash
rtk ./benchmark.sh --allocations propertyaccess simplearithmetic objectcreation arrayops activation-noargs-lite activation-params-lite activation-arguments-lite activation-closures-lite activation-evalscope-lite forloop whileloop ir-arithmetic forofiteration
rtk ./tools/profile propertyaccess --cpu --calltree-depth 25 --calltree-width 20
rtk ./tools/profile simplearithmetic --cpu --calltree-depth 25 --calltree-width 20
rtk ./tools/profile propertyaccess --memory
rtk ./tools/profile simplearithmetic --memory
rtk ./tools/profile objectcreation --memory
rtk ./tools/profile activation-arguments-lite --memory
rtk ./tools/profile forloop --memory
```

## Allocation benchmark rows (current)

```text
profile                    asynkron_ms    asynkron_kb  jint_ms     jint_kb  time_delta             alloc_delta
propertyaccess                     890          286.0      459     87286.2  Jint 1.94x faster      Asynkron 305.15x lower alloc
simplearithmetic                   260        95313.9       71     31876.4  Jint 3.66x faster      Jint 2.99x lower alloc
objectcreation                     699       363863.5      508    181011.3  Jint 1.38x faster      Jint 2.01x lower alloc
arrayops                           654       244969.9      351     38617.1  Jint 1.86x faster      Jint 6.34x lower alloc
activation-noargs-lite             187           32.3      146     59669.4  Jint 1.28x faster      Asynkron 1845.30x lower alloc
activation-params-lite             320           32.6      197     95743.3  Jint 1.62x faster      Asynkron 2933.96x lower alloc
activation-arguments-lite          692       969158.7      287    275795.9  Jint 2.41x faster      Jint 3.51x lower alloc
activation-closures-lite           265       311585.5      306      8811.7  Asynkron 1.15x faster  Jint 35.36x lower alloc
activation-evalscope-lite          369       810605.4      272    274230.6  Jint 1.36x faster      Jint 2.96x lower alloc
forloop                           3033          238.5     3285   1243588.4  Tie                    Asynkron 5213.52x lower alloc
whileloop                          497          245.1      427    118583.6  Jint 1.16x faster      Asynkron 483.72x lower alloc
ir-arithmetic                     1378           76.5      907    488101.8  Jint 1.52x faster      Asynkron 6376.51x lower alloc
forofiteration                     411       201763.2      270    157782.9  Jint 1.52x faster      Jint 1.28x lower alloc
```

## Baseline accounting

- `propertyaccess`: comparable checked-in allocation baseline exists in `docs/performance/propertyaccess-unified-bytecode-production-profile-evidence.md` (`asynkron_kb=302.6`, `jint_kb=87285.7`).
- `activation-arguments-lite`: comparable checked-in allocation baseline exists in `docs/performance/activation-arguments-index-read-fast-path.md` (`asynkron_kb=1019549.5`, `jint_kb=275794.2`).
- `arrayops`: comparable checked-in allocation baseline exists in `docs/performance/arrayops-dense-iteration-callback-args.md` (`asynkron_kb=925094.5`).
- `simplearithmetic`, `objectcreation`, `activation-noargs-lite`, `activation-params-lite`, `activation-closures-lite`, `activation-evalscope-lite`, `forloop`, `whileloop`, `ir-arithmetic`, `forofiteration`: no directly comparable checked-in allocation rows were located during this pass; current rows remain baseline-establishing evidence for this batch.

Propertyaccess baseline vs current (allocation row only):

- Baseline source row: `asynkron_ms=2566`, `asynkron_kb=302.6`, `jint_ms=2195`, `jint_kb=87285.7`, `alloc_delta=Asynkron 288.47x lower alloc`.
- Current row: `asynkron_ms=890`, `asynkron_kb=286.0`, `jint_ms=459`, `jint_kb=87286.2`, `alloc_delta=Asynkron 305.15x lower alloc`.

## Profile excerpts and tooling status

### propertyaccess (CPU)

```text
Speedscope conversion failed: System.FormatException: Failed to read byte[655360] at stream offset 0x7efa
No results to display
```

### simplearithmetic (CPU)

```text
Speedscope conversion failed: System.FormatException: Failed to read byte[655360] at stream offset 0x7efa
No results to display
```

### objectcreation (memory)

```text
Allocation trace parse failed: Failed to read VarUInt32 at stream offset 0x12228
No results to display
```

### activation-arguments-lite (memory)

```text
Metric          Value
Total allocated 953.14 MB

Allocation By Type (Sampled)
Type                 Count      Total
JsSlot[]             1,410  143.35 MB
String               1,329  135.13 MB
PropertyDescriptor     813   82.65 MB
```

### forloop (memory)

```text
Metric          Value
Total allocated 6.72 MB

Allocation By Type (Sampled)
Type                                  Count     Total
JsValue[]                                 2   2.52 MB
String                                   12   1.24 MB
Dictionary<String,PropertyDescriptor>     4 414.41 KB
```

## Scope-limited conclusion

This note captures the exact workload rows and profile command outcomes for this batch. It does not claim broad runtime wins. For the profile surface, `forloop --memory` produced usable output in this worktree, while the other requested profile runs failed at trace conversion/parsing and are recorded as tooling constraints rather than interpreted as runtime regressions.
