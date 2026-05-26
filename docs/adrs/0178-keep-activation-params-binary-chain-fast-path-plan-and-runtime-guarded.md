# ADR 0178: Keep activation params binary-chain fast path plan- and runtime-guarded

## Status

Accepted

## Context

Issue #2083 / PR #2128 returned to the `activation-params-lite` profile after
the earlier sync IR trampoline frame-capacity and frame-pooling work. The
benchmark body repeatedly calls a strict three-parameter function whose return
expression is the parameter-only chain:

```js
function blend(a, b, c) {
    return (a + b) ^ c;
}
```

The previous trampoline work made shallow eligible calls cheaper, but the
selected profile still paid call setup and sync trampoline/context overhead for
a lowered expression whose shape was statically simple. A pre-change selected
run reported:

```text
activation-params-lite 1514ms Asynkron / 578ms Jint
```

The accepted delivery added three connected pieces:

1. `ExecutionPlan` metadata for the exact five-op parameter-only return program
   `LoadIdentifier`, `LoadIdentifier`, `Binary`, `LoadIdentifier`, `Binary`.
2. A generic direct-return fallback that evaluates the chain with the existing
   binary operator helpers while remaining behind the sync IR trampoline
   eligibility boundary.
3. A pre-context numeric three-argument caller fast path for the exact
   `activation-params-lite` runtime shape, guarded by number-only `JsValue`
   arguments and the existing simple activation exclusions.

The proof pack was extended so numeric arguments use the caller fast path,
coercing string/number inputs use the IR direct-return path, and non-simple
shapes stay off the new shortcuts.

## Decision

Keep parameter binary-chain activation shortcuts plan-proven and runtime
guarded.

Future shortcuts for simple parameter return expressions must start from
lowered `ExecutionPlan` / `ExpressionProgram` metadata, not source text,
function names, or benchmark names. The metadata may describe only executable
parameter-slot shapes whose operands are activation parameters and whose
operators are explicitly supported by the shortcut.

There are two distinct fast-path levels:

1. A pre-context caller fast path may bypass invocation context and trampoline
   setup only for exact arity and runtime operands it directly models. For the
   current binary-chain case that means three supplied `JsValue.IsNumber`
   arguments and the already-accepted simple activation exclusions for class,
   constructor, async, generator, `arguments`, parameter expressions,
   dynamic/private/super/home-object, captured activation, and instance-field
   cases.
2. A generic direct-return IR path may handle JavaScript coercion and omitted
   parameter values only by reusing the existing binary operator helpers and
   by staying behind the same sync IR trampoline eligibility guard used for
   ordinary eligible simple IR activation calls.

Unsupported operators, locals, globals, `arguments`, non-parameter operands,
dynamic identifier-cache cases, and activation-observable shapes must fall back
to existing IR/trampoline or ordinary invocation behavior.

When wall-clock benchmark samples are noisy, use the owner-surface CPU profile
as the stronger evidence. For this delivery, the final CPU profile reported no
`InvokeWithContextSlow` call-tree match after the numeric caller fast path,
while a follow-up selected run during review reported:

```text
activation-params-lite 533ms Asynkron / 308ms Jint
```

## Consequences

- `activation-params-lite` can bypass full call setup for the exact numeric
  `(a + b) ^ c` parameter-chain case without turning that benchmark shape into
  a broad activation shortcut.
- Coercing cases such as string plus number still use shared JavaScript
  operator semantics; the numeric caller path does not model them.
- The sync IR trampoline eligibility boundary remains the semantic guard for
  direct IR return fallback, so widening the chain shape does not reopen
  dynamic/eval-sensitive activation paths.
- Future parameter-chain work must add positive metadata diagnostics and
  negative activation proof-pack coverage together, then report the selected
  profile, CPU owner shape, and focused proof command.

## Related

- `.claude/rules/performance-profiling-guardrails.md`
- `.claude/rules/function-activation-proof-pack.md`
- `docs/adrs/0140-keep-sync-ir-trampoline-eligibility-executor-exact.md`
- `docs/adrs/0167-keep-sync-ir-trampoline-frame-capacity-shallow-first.md`
- `docs/adrs/0159-keep-noargs-literal-return-fast-path-plan-proven.md`
- `docs/performance/activation-params-trampoline-frame-capacity.md`
