# Activation Evalscope Eval Program Last-Entry Cache

## Selected profile

- Profile: `activation-evalscope-lite`
- Baseline command: `rtk ./benchmark.sh`
- Baseline signal: `activation-evalscope-lite` reported `1700ms` for Asynkron and `386ms` for Jint, so Jint was `4.40x` faster.
- CPU profile command: `rtk ./tools/profile activation-evalscope-lite --cpu --calltree-depth 40 --calltree-width 40`

The baseline CPU call tree showed repeated direct `eval('y + shared')` calls
spending a large share under `EvalHostFunction.GetOrParseProgram`. The existing
eval program cache was already bounded, but every cache hit still entered the
LRU lock and dictionary path.

## Change

`EvalHostFunction` now keeps a lock-free single-entry cache in front of the
existing 64-entry LRU. Repeated eval of the same source and strictness mode can
return the cached `ProgramNode` without taking the cache lock. Misses and less
predictable eval source patterns continue through the existing bounded LRU.

## Final signal

Repeated selected-profile checks after the change:

- `rtk ./benchmark.sh activation-evalscope-lite`: `468ms`
- `rtk ./benchmark.sh activation-evalscope-lite`: `463ms`
- `rtk ./benchmark.sh activation-evalscope-lite`: `1561ms`
- `rtk ./benchmark.sh --no-build activation-evalscope-lite`: `457ms`
- `rtk ./benchmark.sh --no-build activation-evalscope-lite`: `543ms`
- `rtk ./benchmark.sh --no-build activation-evalscope-lite`: `1001ms`

The post-change median of these Asynkron samples is about `506ms`, a roughly
70% reduction from the `1700ms` baseline signal despite one noisy outlier.
The follow-up CPU profile no longer shows `GetOrParseProgram` as the dominant
eval frame; the remaining top samples moved to activation/environment setup and
boxing under `InvokeWithContextSlow`.

## Validation

- Focused tests: `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~EvalFunctionTests|FullyQualifiedName~ActivationSemanticsProofPackTests" -c Release`
- Result: 76 tests passed.

The change is intentionally narrow: it only changes eval program-cache lookup
and does not alter eval parsing, validation, declaration instantiation, or
execution semantics.

## Follow-up (issue #2228): strict direct eval declaration-free environment reuse

### Change

For strict direct eval, declaration-free programs now evaluate in the already
created strict direct eval lexical environment instead of allocating an extra
empty `eval` child environment. Programs with top-level `var`, function, or
lexical declarations still use the existing eval child-environment path.

### Selected signal

- Baseline reference: `activation-evalscope-lite` Asynkron `1700ms` from this
  document's baseline section.
- Command: `rtk ./benchmark.sh activation-evalscope-lite`
- Result (2026-05-27): Asynkron `511ms`, Jint `606ms` (`Asynkron 1.19x faster`).

Relative to the baseline reference, Asynkron time dropped by `1189ms` (~70%).

### Focused proof

- `rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~EvalFunctionTests|FullyQualifiedName~ActivationSemanticsProofPackTests|FullyQualifiedName~ClassElementEvalTests"`
- Result: 86 tests passed.

## Follow-up (issue #2257): strict direct eval declaration-free no-environment fast path

### Change

For declaration-free strict direct eval in already-strict caller contexts,
`EvalHostFunction` now skips creating both strict-direct lexical and eval
declaration environments and executes the eval program directly in the current
caller environment. Programs with top-level `var`, function, or lexical
declarations still use the existing eval declaration-instantiation path.

### Signals

- Baseline timestamp: `2026-05-27T05:16:45Z`
- Baseline command: `rtk ./tools/profile activation-evalscope-lite --cpu --calltree-depth 40 --calltree-width 40`
- Baseline signal: `InvokeWithContextSlow call-tree root total = 530.35 ms`
- Final timestamp: `2026-05-27T05:19:49Z`
- Final command: `rtk ./tools/profile activation-evalscope-lite --cpu --calltree-depth 40 --calltree-width 40`
- Final signal: `InvokeWithContextSlow call-tree root total = 505.49 ms`
- Signal delta: `-24.86 ms` (lower is better)

The baseline top-functions table included
`EvalHostFunction.InvokeSingleArgument` at `82.93 ms`. After the change it
no longer appears in the top filtered rows, and the follow-up call tree reports
`EvalHostFunction.InvokeSingleArgument` at `21.10 ms` under direct-eval calls.

### Focused proof

- `rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~EvalFunctionTests|FullyQualifiedName~ActivationSemanticsProofPackTests|FullyQualifiedName~ClassElementEvalTests"`
- Result: `89` tests passed.

### Scope note

This slice is narrow: it keeps eval parse/cache lookup, validation, private-name
checks, strict-reserved checks, and declaration behavior intact, and avoids
marking caller environments as eval declaration environments on the no-env
fast path.
