# JSON default data properties and quote fast path

Issue: `autrun-discqnniowqw-45a6add5dd`

## Baseline

The automation baseline started with the required comparison matrix:

```bash
rtk ./benchmark.sh
```

In that run `json` was one of the top clean synchronous profiles where Jint beat
Asynkron:

```text
json                           8481     1973  Jint 4.30x faster
```

A repeated focused timing before editing was:

```bash
rtk ./tools/compare-jint-profiles --no-build json
```

```text
json                           4894     1883  Jint 2.60x faster
```

The selected CPU profile was captured with the required profile command:

```bash
rtk ./tools/profile json --cpu --calltree-depth 40 --calltree-width 40
```

The useful comparison-harness calltree was also captured with the same runner
arguments used by `compare-jint-profiles`:

```bash
rtk asynkron-profiler --cpu --calltree-depth 40 --calltree-width 40 --filter Asynkron.JsEngine -- tools/ProfileRunner/bin/Release/net10.0/ProfileRunner --wrap-iife json
```

The profile put the bounded owner surface in `JsonHelper`, especially repeated
`ParseJsonValue` default property creation and compact string quoting.

## Change

`JSON.parse` now uses the engine's internal default data-property helper when it
creates fresh ordinary JSON objects, reviver root wrappers, and reviver source
context objects. That avoids allocating and completing full property descriptors
for the common configurable/writable/enumerable data-property case while still
creating own data properties instead of invoking `__proto__` setters.

`JSON.stringify` now has a compact-string fast path in `QuoteString`. Strings
without quotes, backslashes, controls, or surrogate code units return a quoted
string directly instead of allocating and walking a `StringBuilder` path.

## Final signal

After the change, warm repeated timings were:

```bash
rtk tools/ProfileRunner/bin/Release/net10.0/ProfileRunner --asynkron --force-timing --wrap-iife json
```

```text
Done in 3745ms (avg 187.25ms per iteration)
Done in 3801ms (avg 190.05ms per iteration)
```

The comparison harness reported:

```bash
rtk ./tools/compare-jint-profiles --no-build json
```

```text
json                           3799     1804  Jint 2.11x faster
```

Compared with the focused `4894 ms` pre-edit run, the selected benchmark
improved by about 22%.

## Verification

Focused JSON tests:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~FoundationTests.JSON"
```

Result: 6 tests passed with existing warnings.
