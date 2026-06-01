# Startup SLO evidence packet

Date: 2026-06-01
Issue: #2935
Selected target: Cold-start latency (`startup`)

## Machine context

- Host: `Plutten.local`
- OS: macOS 26.3 (`25D125`), Darwin 25.3.0, arm64
- CPU: Apple M1
- Cores: 8
- Memory: 16 GiB
- .NET SDK: 10.0.100
- Node.js: v25.8.1
- Worktree commit before this evidence note: `c32088d07`

## Target and baseline

The selected SLO target from `docs/dreaming.md` is cold-start latency under
`5 ms` p95 on commodity hardware, measured by ProfileRunner `startup`.

The committed regression baseline remains `tools/perf-slo-baseline.md`:

```text
startup 4.80
```

That baseline is an avg-ms guardrail for `make slo-gate`. It is not p95 proof
and it is not a Node.js parity claim.

## Commands run

```bash
rtk ./benchmark.sh startup
rtk ./benchmark.sh --allocations startup
rtk ./tools/check-slo-gate --no-build
```

The `--no-build` SLO-gate run reused the ProfileRunner binary built by the
preceding `benchmark.sh` commands.

## Benchmark matrix rows

Timing row:

```text
profile                 asynkron_ms  jint_ms  delta
startup                          31        1  Jint 31.00x faster
```

Allocation row:

```text
profile                 asynkron_ms    asynkron_kb  jint_ms     jint_kb  time_delta             alloc_delta
startup                          25        23395.2        1       161.9  Jint 25.00x faster     Jint 144.52x lower alloc
```

## SLO gate evidence

The same SLO gate run measured the current avg, derived p95, and same-run
Node.js reference timing:

```text
profile         baseline_avg_ms     current_avg_ms current_p95_ms    hard_ceiling_ms    delta_% baseline     target_evidence     node_avg_ms same_run
startup                    4.80               3.00           5.14              14.40      -37.5 ok           p95-over-target          0.2685 ratio=11.17x
microtask                  8.00               5.20           6.93              24.00      -35.0 ok           avg-over-target          0.2634 ratio=19.74x

OK: no SLO timing regression beyond 200% tolerance.
Note: p95 target status and same-run Node.js comparison are non-failing evidence; only committed avg-ms baseline regression fails this gate.
```

Only `startup` is the selected target for this packet. The `microtask` row is
included because `tools/check-slo-gate` reports the full committed SLO gate set.
This issue does not advance microtask status.

## Status conclusion

Startup now has a maintained, command-complete local evidence packet with:

- committed avg baseline: `4.80 ms`
- current avg signal: `3.00 ms`
- derived p95 signal: `5.14 ms`
- same-run Node.js reference: `0.2685 ms` (`Asynkron/Node = 11.17x`)
- same-run Jint timing row from `benchmark.sh`: `31 ms` vs `1 ms`
- same-run Jint allocation row from `benchmark.sh --allocations`: `23395.2 KB`
  vs `161.9 KB`

The selected SLO remains directional / Prototyped, not met: the current
`5.14 ms` p95 is above the `< 5 ms` target, and both same-run Node.js and Jint
comparison rows show startup is still materially slower than the reference
runtime for this workload. The committed avg-ms regression guard remains green
and unchanged.

## Post-edit gate verification

After writing this evidence packet and updating the startup-only roadmap and
dreaming references, the canonical make target stayed green:

```bash
rtk make slo-gate
```

```text
profile         baseline_avg_ms     current_avg_ms current_p95_ms    hard_ceiling_ms    delta_% baseline     target_evidence     node_avg_ms same_run
startup                    4.80               3.10           5.37              14.40      -35.4 ok           p95-over-target          0.2506 ratio=12.37x
microtask                  8.00               5.00           6.46              24.00      -37.5 ok           avg-over-target          0.3649 ratio=13.70x

OK: no SLO timing regression beyond 200% tolerance.
Note: p95 target status and same-run Node.js comparison are non-failing evidence; only committed avg-ms baseline regression fails this gate.
```
