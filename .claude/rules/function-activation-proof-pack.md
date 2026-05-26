# Function Activation Proof Pack

Before changing function-call activation setup, run the named internal proof pack
for activation semantics:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ActivationSemanticsProofPackTests"
```

## Rules

1. Treat activation changes as semantically high-risk even when the goal is only
   overhead reduction. The proof pack must stay green before claiming the change
   preserves JavaScript behavior.
2. Keep the pack broad across activation families: sloppy and strict
   `arguments`, parameter aliasing, default/rest/destructured parameters, nested
   closures, direct eval, `with` / dynamic scope, strict vs sloppy `this`,
   generators, async functions, and async generators.
3. If activation work changes one of those semantics, update
   `ActivationSemanticsProofPackTests` in the same delivery so the named filter
   remains the focused confidence gate for future agents.
4. Do not replace this narrow internal proof with Test262-only evidence. Test262
   can widen confidence after the focused pack passes, but the named pack is the
   fast regression gate for this subsystem.
5. When optimizing lazy `arguments` creation, prove the observable-binding split
   explicitly: ordinary body `arguments`, parameter-default `arguments`, direct
   eval in the body, direct eval in parameter defaults, nested-arrow
   `arguments`, and nested-arrow direct eval. Arrow functions inherit the
   enclosing activation's `arguments`; nested non-arrow functions are the
   boundary.
6. Keep `argumentsObjectNeeded` separate from `NeedsArgumentsBinding`.
   `argumentsObjectNeeded` is the spec activation decision for creating and
   protecting the arguments object/binding; `NeedsArgumentsBinding` is an
   optimization guard for whether ordinary execution can observe that binding.
   Do not gate arguments-object creation solely on `NeedsArgumentsBinding`, and
   do not let hoisted `var arguments` replace an existing non-lexical
   `arguments` object binding with `undefined`.
7. When optimizing arity-specific sync calls, keep struct argument carriers on
   concrete generic paths until parameter binding consumes them. Do not pass
   `TwoValueArgs`, `ThreeValueArgs`, or similar readonly struct lists through
   `IReadOnlyList`-typed hot helper parameters or locals, because that boxes the
   struct and reintroduces the allocation the optimization is trying to remove.
   If the arity reduction is for an Array iteration callback, also prove the
   callback cannot observe omitted arguments before switching from the full
   `(value, index, array)` carrier to a narrower carrier. Ordinary functions,
   rest parameters, parameter expressions, async/generator callbacks, and
   callbacks with explicit index or array parameters must stay on the full
   observable argument path.
8. When binding parameters into an activation that already has slot storage,
   update the planned parameter slots directly. Do not call
   `DefineParameterFast` as a closure mirror for those parameters; it appends a
   new binding and can reintroduce per-invocation `JsSlot[]` growth. Use
   `DefineParameterFast` only for dictionary/no-slot fallback paths or real
   appended activation bindings.
9. When pre-sizing function or parameter environments, count only activation
   bindings that the runner will append on that exact path. Capacity reservations
   may avoid `GrowSlots`, but they must not change logical slot count, binding
   order, or whether a binding exists.
10. Keep activation fast-path assertions tied to stable behavior, negative
   fallback signals, or measured allocation evidence. Do not require optional
   activation trace logs when the optimized path may validly skip creating an
   activation object or omit the trace.
11. When skipping concrete `JsArgumentsObject` materialization for performance,
    keep the optimization guarded by both `argumentsObjectNeeded` and
    `NeedsArgumentsBinding`. The former owns the spec-shaped activation
    decision; the latter owns whether the binding/object can be observed on the
    current path. Do not replace this with a direct body scan, and do not weaken
    direct eval, parameter-default, dynamic-scope, nested-arrow, or hoisted
    `var arguments` proofs.
12. When changing script-mode FunctionCode IR gating, invocation-environment
    pooling, or recursive activation reuse, prove both sides of the boundary:
    `Name=FunctionCode` for declaration/parameter/arguments instantiation
    semantics and the focused strict same-function tail-call test for stack
    stability. Function declaration conflicts with parameters or `arguments`
    are activation-isolation signals; they are not permission to disable all
    strict recursive IR fast paths.
13. When allowing arrow functions onto simple IR activation paths, require the
    lowered simple return `ExpressionProgram` to prove that the arrow has no
    lexical `this`, lexical `new.target`, or `super` dependency. Do not use
    source size, parameter count, or callback arity alone as permission for the
    activation shortcut. Pair the positive simple-arrow value-semantics proof
    with negative lexical binding coverage so dependency-bearing arrows keep
    full arrow invocation semantics.

## Why

Issue `planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-p-8b2aee3a48`
and PR #1636 added `ActivationSemanticsProofPackTests` after the
function-call activation overhead plan identified many easy-to-break edge cases.
Ordinary functions and generator/async-generator activation paths are separate,
and mapped `arguments`, non-simple parameters, direct eval, `with`, mode
differences, and resumable functions can regress independently. Future
activation-overhead work needs one explicit, cheap proof gate before broader
quality or Test262 runs.

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-p-3c8725f1f9`
and PR #1637 showed the lazy-arguments trap directly: an optimization that only
looks for syntactic `arguments` in the immediate body misses direct eval and
nested arrows, both of which can observe the enclosing function's binding. The
durable rule is to prove the observable-binding decision, not just allocation
avoidance.

Related ADR: `docs/adrs/0100-keep-observable-arguments-binding-eval-aware-through-arrows.md`.

Issue #1750 / PR #1789, reviewed again during the #gh1806 red-main learn pass,
showed the inverse lazy-arguments trap directly:
`function f() { return typeof arguments; var arguments = 42; }` still needs the
sloppy function arguments object at the return point even though the later
`var arguments` declaration is unreachable and has no executed initializer. The
runtime must create/protect the spec arguments object from
`argumentsObjectNeeded`, define it in the body/execution environment when slot
layout could otherwise shadow it, and preserve that binding during var hoisting.
Future work in this area should prove both the internal hoisting regression
(`HoistingTests.VarDeclaration_ShouldNotOverride_ArgumentsObject_BeforeReturn`)
and the activation proof pack, then use focused Test262 coverage when the
failing file is available.

Related ADR: `docs/adrs/0100-keep-observable-arguments-binding-eval-aware-through-arrows.md`.

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-s-f3dc144c31`
and PR #1657 showed the arity-carrier trap directly: after the simple sync
activation fast path landed, `functioncalls-lite --memory` still reported
`TwoValueArgs` / `EmptyValueArgs` helper allocations until the typed call path
preserved generic struct carriers through `SyncFunctionInvoker` and used
`Array.Empty<JsValue>()` for the runner placeholder. Future activation-overhead
work should prove both the activation proof pack and the allocation table for
helper carriers.

Related ADR:
`docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`.

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-s-8f7318b9d0`
and PR #1648 showed the slot-backed closure trap directly: after activation
slots were initialized to the planned shape, duplicate `DefineParameterFast`
calls in parameter binding appended extra parameter slots on every invocation.
The focused proof pack stayed green after writing the existing slots directly,
and `functioncalls-lite --memory` showed `JsSlot[]` sampled allocations dropping
from the previously recorded roughly 57k range to 7,781x while `arrayops`
remained stable at 170x. Future activation-overhead work should pair the proof
pack with allocation evidence for `JsSlot[]` / `GrowSlots` whenever it changes
slot-backed binding.

Related ADR:
`docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`.

Issue `autrun-dir4p2zvmkps-4dd788bea7` and PR #1740 showed the activation
backing-capacity trap in the `classdef` profile: class constructors repeatedly
grew slot arrays while appending predictable function/parameter environment
bindings. The accepted fix kept binding order unchanged and only reserved enough
backing capacity for known appends, then proved the change with class-focused
tests, slot/environment tests, runner AST-seam scan, and `forloop --memory`.
Future activation sizing work should keep that distinction explicit.

Related performance note:
`docs/performance/classdef-ir-environment-pre-sizing.md`.

Issue `autrun-dirl74ca7a0g-8d6fc2682c` / PR #0 applied the same carrier rule
outside activation setup by routing array iteration callbacks through
`ThreeValueArgs`. The reusable lesson is unchanged: the struct is only
allocation-free while it stays concrete through the hot callback path.

Issue `autrun-dis3ezcjxsm0-238752b986` / PR #1949 showed the next trap in the
same callback path: after `ThreeValueArgs` removed array allocation, the
`classdef` profile still paid for index/array callback arguments that simple
arrow callbacks could not observe. The fix was deliberately narrower than a
generic callback-length shortcut: only non-async, non-generator arrows with zero
or one simple identifier parameter and no parameter expressions use
`SingleValueArgs`. The full three-argument path remains the semantic owner for
ordinary functions that expose `arguments`, rest parameters, and callbacks that
name the index or array. Future callback-arity optimizations should pair the
profile evidence with positive value-semantics tests and negative observable
extra-argument tests.

Related ADR:
`docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`.

Issue #1754 / PR #1759 first corrected the proof-pack trace-log trap by
renaming the affected test to match its stable negative fast-path assertions
after focused attempts to require a positive activation trace log failed.
Issue #1758 / PR #1762 then confirmed the same trap through a quality-gate
failure because
`ActivationSemanticsProofPackTests.SimpleSyncFunction_UsesIrActivationFastPath`
required an activation trace log that was no longer guaranteed after valid
activation fast-path work. The repair kept the semantic proof focused by
asserting stable negative fast-path signals instead of the optional trace.
Future activation tests should prove the behavior that must stay true and avoid
turning diagnostics into contractual runtime output.

Issue `autrun-dirquxckeg74-0fe6957821` / PR #1811 optimized the `classdef`
profile by skipping `JsArgumentsObject` allocation for functions where
`argumentsObjectNeeded` is spec-eligible but `NeedsArgumentsBinding` proves the
binding is unobservable. The durable lesson is that lazy materialization is a
profile-owned activation optimization, not a simplification of the arguments
semantics split from ADR 0100. Future work should preserve the two-decision
shape and prove the observable-binding cases explicitly before claiming an
arguments-object allocation win.

Related ADR:
`docs/adrs/0124-keep-lazy-arguments-object-materialization-observable-and-profile-owned.md`.

Issue #1866 / PR #1921 fixed Test262 `FunctionCode` execution-context rows by
treating function-declaration/parameter conflicts and recursive observable
activation reuse as narrow fast-path eligibility signals. The repair bounced
through a quality-gate stack overflow when the guard became too broad and
blocked strict same-function tail-call handling. The durable activation lesson
is to prove FunctionCode instantiation semantics and tail-call stack behavior
together whenever recursive activation reuse or script-mode IR gating changes.

Related ADR:
`docs/adrs/0146-keep-functioncode-activation-isolation-ahead-of-ir-fast-paths.md`.

Issue `autrun-dis8iqooxge8-6666a730d6` / PR #1985 showed the next `classdef`
callback activation trap: after callback arity was narrowed, the simple arrow
`dogs.map(d => d.speak())` still paid full invocation because all arrows were
rejected from simple IR activation. The accepted fix permits only arrows whose
simple return bytecode has no `this`, `new.target`, or `super` operations, and
keeps dependency-bearing arrows on the full path. Future simple-arrow
activation work should scan the lowered expression program, prove positive
callback value semantics, and pin negative lexical binding behavior.

Related ADR:
`docs/adrs/0150-keep-simple-arrow-ir-activation-lexical-dependency-guarded.md`.
