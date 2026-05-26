# ADR 0171: Keep no-spread construct argument carriers and super spread order

## Status

Accepted

## Context

Issue #2077 / PR #2093 selected `classdef` because the current allocation
benchmark still showed a large constructor-heavy gap: the build-stage baseline
recorded Asynkron at `1271 ms / 704351.6 KB` versus Jint at
`540 ms / 43647.9 KB`. The memory and CPU profiles pointed at constructor and
`super(...)` construction as a narrow owner surface, with residual allocation
still elsewhere in contexts, environments, runner setup, method calls, callback
dispatch, and object/property work.

The accepted optimization split no-spread expression-program construction from
spread/generic construction. Common no-spread `new` and derived `super(...)`
calls with arities 0 through 4 now pass concrete argument carriers
(`EmptyValueArgs`, `SingleValueArgs`, `TwoValueArgs`, `ThreeValueArgs`, and
`FourValueArgs`) into a generic `ReflectHelper.Construct<TArgs>` path, avoiding
temporary `JsValue[]` materialization while keeping proxy, host, target, and
`newTarget` checks in the shared construction helper.

Review then caught a semantic regression in the first delivery. The fixed-arity
split had made `ExecuteProgramSuperConstruct` surface the non-constructor
super-base `TypeError` before materializing spread arguments. For
`class extends null { constructor() { super(...iterable); } }`, ECMAScript still
observes the spread iterable work before the final non-constructor failure; if
the iterable throws, that thrown error must be the visible completion. The
repair materialized spread arguments before the non-constructor super-base
check while preserving the no-spread fast path.

## Decision

Keep no-spread construct and `super(...)` argument fast paths separate from
spread construction.

For no-spread expression-program construction:

1. use arity-specific concrete carriers through the typed
   `ReflectHelper.Construct<TArgs>` boundary for common arities;
2. keep uncommon higher-arity calls on the explicit materialized-array fallback;
3. preserve `target` / `newTarget`, proxy, host constructor, and
   constructor-hook behavior already owned by the shared construction helper;
   and
4. prove observable argument order, `arguments.length`, and `new.target`
   forwarding for direct constructors and derived `super(...)`.

For spread construction:

1. do not treat spread as merely a late fallback after constructor validation;
2. materialize spread arguments before the `super(...)` non-constructor
   TypeError path so iterator side effects and iterator-thrown errors preserve
   spec order;
3. return pooled argument arrays on every completion path; and
4. prove both side-effect-only and throwing spread iterables when the super base
   is not constructable.

## Consequences

- Future `classdef` or construction-performance slices may optimize the
  no-spread hot path without reallocating the spread path, but they must keep
  the two observable orderings distinct.
- Performance evidence for no-spread construct calls does not prove spread
  behavior. Spread calls need their own semantics proof because iterators can
  run user code before the final construction error is reported.
- Constructor helper changes in this area should continue to respect ADR 0032
  target/newTarget role separation, ADR 0101 typed argument-carrier rules, ADR
  0149 constructor-family hook separation, and ADR 0164 JsValue-native
  super-constructor resolution.
- A focused proof pack for this surface should include the fixed-arity
  constructor/super observability test, the spread-super ordering regression,
  the `class extends null` super-call error case, the AST-eval seam scan, and
  current classdef benchmark/profile evidence when claiming a performance win.

## Related

- `.claude/rules/ecmascript-abstract-operations.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `docs/adrs/0032-keep-reflect-construct-target-allocation-newtarget-prototype-split.md`
- `docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`
- `docs/adrs/0149-keep-prototype-constructor-newtarget-hooks-split.md`
- `docs/adrs/0164-keep-super-constructor-resolver-jsvalue-native.md`
