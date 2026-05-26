# ADR 0176: Keep sync IR activation environment pooling ownership guarded

## Status

Accepted

## Context

Issue #2080 / PR #2121 targeted allocation pressure in three small sync
microprofiles:

```text
profile            baseline_kb  final_kb
simplearithmetic      105169.6   99310.3
closures-lite          26714.2   21790.9
recursion-lite        432417.0  362525.5
```

The profiling work separated the owners instead of assuming one benchmark
surface explained all three. `closures-lite` and `recursion-lite` shared a
large sync invocation and IR activation owner: repeated function calls created
short-lived activation environments and, in the ordinary case, a fresh
evaluation context. `simplearithmetic` still had mostly separate parse, host,
and reflection noise, but the wrapped runner path benefited from the same
reduced sync activation setup.

The accepted delivery pooled transient sync IR invocation environments and
reused the caller-owned evaluation context when the activation did not need
private-name scope replay. It also applied the same transient environment
return ownership to the simple IR activation fast path. The risky boundary was
not the pool call itself, but proving which objects were owned by the current
invocation and which could outlive it through closure capture, script-mode
state, async/generator suspension, class constructor semantics, or private-name
context state.

## Decision

Keep sync IR activation environment pooling ownership-guarded.

Activation environments may be rented and returned only when the current sync
ordinary invocation owns the complete transient chain being returned. The
runner must keep ordinary creation for script mode, generators, async functions,
async generators, and class-constructor activation shapes unless a future proof
models their lifetime explicitly.

Pool return must stop before the caller closure root. Any environment that can
outlive the invocation through closure capture must remain protected by the
existing captured-environment checks instead of being treated as a temporary
activation object. A closure that increments a captured counter across repeated
pooled invocations is the minimum regression shape for this boundary.

EvaluationContext reuse is a separate ownership decision from environment
pooling. A sync IR activation may reuse the caller-owned context only when no
private-name scope or captured private-name scope replay must be entered for the
callee. Nested fast paths must not return a caller-owned context; context
lifetime stays with the outer invocation cleanup path.

The simple IR activation fast path may rent and return its transient function
and body environments only after the existing simple-activation eligibility and
plan-shape checks pass. Do not widen this by benchmark name, source text, or a
generic "small function" predicate.

Future changes on this boundary should prove:

1. the selected memory profile still names sync invocation or activation
   environment/context setup as the owner;
2. closure capture keeps captured state across pooled activation reuse;
3. recursive binding and strict self-name shadowing fallbacks still behave;
4. private-name, async/generator, script-mode, class-constructor, `super`,
   home-object, and dynamic activation shapes stay off the shortcut unless
   separately modeled; and
5. allocation evidence comes from a before/after
   `rtk ./benchmark.sh --allocations ...` run, not from a single sampled call
   tree alone.

## Consequences

- `closures-lite` and `recursion-lite` avoid repeated allocation of transient
  sync activation environments while preserving captured closure state.
- Context reuse remains limited to callee shapes that do not need their own
  private-name scope stack.
- Environment pooling remains an ownership and lifetime change, not a
  mechanical replacement for `JsEnvironment.CreateInstance`.
- Future sync activation pooling work must carry focused closure and recursive
  binding proofs alongside allocation evidence before claiming a retained win.

## Related

- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `docs/adrs/0146-keep-functioncode-activation-isolation-ahead-of-ir-fast-paths.md`
- `docs/adrs/0150-keep-simple-arrow-ir-activation-lexical-dependency-guarded.md`
- `docs/adrs/0159-keep-noargs-literal-return-fast-path-plan-proven.md`
