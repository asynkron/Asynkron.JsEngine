# JavaScript Spec Property Access in C# Helpers

When implementing ECMAScript algorithms that say `Get(O, P)`, use a runtime
property access path that observes inherited properties, accessors, proxies, and
JavaScript throws. Do not replace `Get` with own-element storage reads just
because the receiver is array-like.

## Object Environment Writeback

For `with` object-environment bindings, keep the captured binding target and
the current strict/sloppy writeback rules separate:

- direct assignment must capture the object-environment reference before
  evaluating the RHS, then write through that captured reference afterward;
- strict writeback for a captured object binding must re-check `HasProperty`
  before `Set` when missing assignment is not explicitly allowed;
- if a getter, RHS side effect, compound assignment, or update operator deletes
  the binding before writeback, throw `ReferenceError` instead of recreating the
  property through the generic property setter path;
- identifier update fast paths must fall back to assignment-reference lookup
  when identifier caching is disabled or a `with` object is in the active
  environment chain;
- preserve sloppy-mode recreate-after-delete behavior only through an explicit
  sloppy/allow-missing path.
- simple `var` declarations with initializers must also resolve the binding
  reference before evaluating the initializer. If the initializer mutates the
  `with` lookup surface, write back through the pre-resolved reference rather
  than resolving the name again after the initializer.

Add focused tests for both sides when touching this area: strict missing-binding
writeback must throw after the side effect that deletes the binding has already
run, while sloppy captured-binding writeback may recreate the property.

## Destructuring Binding Target Order

For compiled object destructuring binding, keep the observable ordering explicit:

- evaluate any computed source property name first;
- resolve or capture the binding target at the binding-target step;
- only then read the source property and evaluate a default initializer;
- write through the captured binding target when one was resolved.

This is not just an optimization detail. `with` environments can observe target
lookup through `has`, source properties can observe getter side effects, and
defaults can observe later name lookups. Do not move var target lookup after
source property access/default evaluation, and do not repair this class of bug
by adding an AST-evaluation fallback to the compiled binding runner.

## Binding Target Abrupt Completion

When a compiled binding-target program is applied from a declaration
instruction, keep JavaScript throws inside the active `EvaluationContext` and
the declaration instruction's abrupt-completion path.

- catch internal `ThrowSignal` at the declaration boundary when applying
  binding target programs;
- convert it to `context.SetThrow(...)` if the context is not already throwing;
- let the declaration handler's existing throw slow path perform `try`/`catch`,
  iterator-close, and final rethrow behavior;
- do not let `ThrowSignal` bubble out as a host/runtime crash for negative
  destructuring fixtures.

This applies to generic binding target programs for nested array/object binding,
defaults, exhausted iterators, and name inference. Prove this class with the
owning focused Test262 method group rather than a broad suite run.

For `catch` parameter destructuring, a binding-time throw is a replacement
throw raised while the current catch environment and try/catch frame are active.
Run that throw through the IR abrupt-completion path from the catch instruction
instead of jumping directly to the catch instruction's `Next`. That preserves
iterator-close/finally cleanup for the active destructuring iterator and lets
the replacement throw bubble to an outer handler. WHY: issue #1753 / PR #1795
fixed `Statements_try_dstr` crashes where a catch-array default initializer
throw skipped the cleanup/outer-catch path for the thrown iterable.

For class-method parameter destructuring with array-pattern defaults, abrupt
default initializers must preserve both observable outcomes: the original
JavaScript throw reaches the caller, and the active iterator's `return()` hook
runs exactly once. Do not accept a regression that checks only the close side
effect or only the thrown value.

## Destructuring Slot-Proven Fast Paths

When optimizing destructuring binding targets with direct slot writes, treat
slot metadata as a provenance proof, not as permission to skip binding
semantics.

- stamp identifier binding target programs and simple array destructuring
  element/rest targets only from analyzer/lowering-owned scope metadata;
- rewrite catch binding target programs only after the catch scope is visible on
  the rewriter scope stack, so catch targets cannot stamp against an outer
  lexical binding;
- at runtime, revalidate the flat slot, scope id, and slot name before writing;
- in assignment mode, fall back for disabled identifier cache, `with` chains,
  immutable bindings, global constants, special bindings, or any target whose
  generic assignment path owns behavior;
- in declaration mode, write directly only for uninitialized lexical let/const
  slots that are non-special. Var binding, unsupported slot state, and
  unstamped targets must stay on the generic binding helpers.

WHY: issue #2054 / PR #2070 optimized slot-proven destructuring binding targets,
but review/build-back repairs caught that catch binding programs were first
stamped before their catch scope was on the rewriter stack, and that assignment
direct-slot writes initially skipped immutable named function expression
self-bindings and global constant semantics. Future changes on this surface
must prove both the positive metadata path and the negative semantic fallback
path.

## Super Property Reference Order

For expression bytecode that touches `super.property` or `super[expr]`, keep the
super-reference validation before any observable property-key work:

- emit or execute `EnsureSuperReference` before evaluating computed property
  keys;
- only evaluate `super[expr]` keys after the derived constructor has initialized
  `this`;
- keep the final operation-specific error, such as delete-super
  `ReferenceError`, after the key side effects that are valid for an initialized
  `super` reference.

This applies even when the operation always throws. The throw does not erase the
observable ordering before it.

## Descriptor Delete Semantics

For ordinary JavaScript `delete obj.prop` and `delete obj[key]`, preserve the
descriptor-aware delete result and interpret it with the active strictness:

- configurable own data and accessor descriptors must be removed and return
  `true`;
- non-configurable own descriptors must remain present and return `false`;
- strict mode must convert a failed descriptor delete into a JavaScript
  `TypeError`;
- sloppy mode must expose the non-throwing `false` completion;
- do not use internal force-delete helpers to implement ordinary JavaScript
  `delete`.

Pair descriptor-shape coverage with strict/sloppy coverage when touching this
area. The issue #1751 / PR #1790 incident showed that data/accessor
configurability and strictness are separate axes: tests that cover only simple
object-literal properties can miss descriptor-backed delete crashes.

## Descriptor-Preserving Assignment Fast Paths

When optimizing ordinary property assignment, bypass descriptor materialization
only when the candidate write is proven to be an existing writable own data
property update on the same receiver/target object.

- require receiver identity, such as `ReferenceEquals(receiverObject, target)`;
- use a helper that fails for missing, accessor, non-writable, inherited, proxy,
  exotic, or otherwise descriptor-sensitive writes;
- preserve the generic descriptor/prototype path for strict-mode failure,
  inherited setters, non-target receivers, and typed-array exotic behavior; and
- pair the positive fast path with tests for descriptor flag preservation and
  negative strict-mode non-writable behavior.

WHY: issue gh2843 / PR #2846 removed the `PropertyDescriptor` allocation owner in
computed symbol assignment by adding a guarded `TrySetExistingJsValue(...)` path.
The guard matters because descriptor allocation was the cost, but descriptor
semantics still own accessors, read-only writes, prototype setters, and receiver
identity. Related ADR:
`docs/adrs/0303-keep-computed-symbol-assignment-descriptor-fast-path-guarded.md`.

## Computed Member Nullish Read Order

For expression bytecode that lowers ordinary computed reads such as
`base[key]`, keep the observable steps split:

- evaluate the base expression;
- evaluate the computed key expression;
- require the base to be object-coercible;
- resolve the property key only after the nullish-base check succeeds;
- then perform the property read.

For nullish bases, the key expression's side effects must still happen, but
property-key conversion must not happen after the `TypeError`. For optional
computed reads such as `base?.[key]`, keep the separate optional-chain contract:
a nullish base short-circuits before the key expression runs.

Do not repair this class of issue by routing computed member reads through the
legacy AST evaluator. The expression bytecode compiler/runner owns the
spec-ordered split and must carry the metadata needed to distinguish ordinary
nullish TypeError behavior from optional chaining.

## Compound Indexed Assignment Nullish Order

For expression bytecode that lowers compound indexed assignments such as
`base[key] *= rhs`, keep the reference steps separate and ordered:

- evaluate the base expression;
- evaluate the computed key expression;
- require the base to be object-coercible before converting the key;
- resolve the property key exactly once;
- read the old value, evaluate the RHS, apply the compound operator, and write
  the result back through the captured reference.

When the base is not at the top of the expression-program stack, use an explicit
operation that checks the correct stack depth instead of reordering the stack by
running `ToPropertyKey` first. Do not repair this class of issue by routing the
compound assignment through the legacy AST evaluator.

## Computed Member Update Nullish Order

For expression bytecode that lowers computed update expressions such as
`base[key]++`, keep the reference steps separate and ordered:

- evaluate the base expression;
- evaluate the computed key expression;
- require the base to be object-coercible before converting the key;
- resolve the property key only after the nullish-base check succeeds;
- then read, numerically update, and write back through the captured reference.

The nullish-base `TypeError` must win over observable property-key conversion.
This differs from the key expression itself, which ordinary computed member
syntax still evaluates before the nullish-base check. Do not collapse those two
steps into one `ToPropertyKey` call ahead of the base check.

## Nullable Throw State

If the access helper accepts an optional `EvaluationContext`, check nullable
throw state explicitly:

```csharp
if (evalContext?.IsThrow is true)
{
    throw new ThrowSignal(evalContext.FlowValue);
}
```

Avoid `== true` for this pattern. It is easier to miss during review and was
the concrete cleanup requested after the issue #751 Array.prototype.at fix.

## Native Reentrancy Guards

Keep native reentrancy guards out of JavaScript-visible property storage.

When a built-in such as an Array prototype mutator needs to prevent recursion
from an observable getter, setter, proxy trap, or callback, store the guard in
private runtime state keyed by the receiver/accessor identity and clear it in
`finally`. Do not write marker properties such as `__inPush__` onto arrays or
array-like receivers.

These guard writes are not harmless implementation details. JavaScript can
observe them through `HasProperty`, `Get`, `Set`, enumeration, proxy traps, and
array length interactions.

## Built-In Copy Operations Use Set

When a built-in copies values into an existing target object, preserve the
spec's write operation. For `Object.assign`, each enumerable source property
write is `Set(to, key, value, true)`, not `CreateDataProperty` or
`DefineProperty`.

This distinction is observable:

- an existing accessor property on the target must invoke its setter even when
  the target is non-extensible, sealed, or frozen;
- Symbol keys follow the same write path as string keys;
- failed target writes, such as missing properties on non-extensible targets or
  non-writable data properties, still throw.

Do not "simplify" copy helpers into descriptor creation on the target unless the
ECMAScript algorithm explicitly calls for that operation. Pair this class of
change with focused regressions for accessor targets and failed writes.

## Array Mutator Move/Delete Helpers

When Array prototype mutators shift or compact indexed elements, preserve the
spec-observable move-or-delete operation:

- check the source element through the existing observable element-read helper;
- when the source exists, write the target through the same `Set` path the
  mutator already used;
- when the source is absent, check whether the target property exists, then run
  the same delete-or-throw helper with the active method name and realm;
- do not turn sparse holes into `undefined` writes, skip target deletion, use
  direct dense-storage writes, or hide delete failures; and
- share only the exact invariant move/delete operation. Keep loop direction,
  bounds, length updates, result construction, insertion, and trailing cleanup at
  the owning mutator call site unless a focused proof shows those steps are also
  identical.

For recurring code-reduction slices on this surface, prefer a named local helper
with explicit `accessor`, `objectLike`, `fromKey`, `toKey`, and `methodName`
parameters over delegate-based loop extraction in hot paths. Prove the affected
Array methods with a focused internal test filter plus the usual code-size and
diff checks.

## Data Property Helper Value Carriers

When a built-in creates data properties and the value is already carried as a
`JsValue`, use the `JsValue` helper overload instead of routing through an
`object?` helper.

Examples:

- use `CreateDataPropertyOrThrowJsValue(result, key, value, realm, method)` when
  `value` is a `JsValue` from arguments, property reads, iterator results, or
  mapper callbacks;
- for result-object builders that are intentionally migrated to the typed helper,
  wrap raw host primitives with `new JsValue(...)` at the callsite and preserve
  the existing property order;
- after the selected helper cluster has no remaining internal `object?` callers,
  delete the broader overload rather than keeping a dead fallback that future
  callsites can accidentally choose;
- keep the same spec operation and ordinary fallback behavior. This rule is
  about preserving the typed value carrier and avoiding avoidable boxing, not
  about bypassing property-definition semantics.

## Why

Issue #751 fixed `Array.prototype.at` after the direct array-element path failed
Test262 semantics for sparse holes, inherited indexed properties, and throwing
getters. The durable lesson is not specific to `at`: spec-level `Get` must be
implemented as observable JavaScript property access, and the C# nullable
throw-state check must propagate the JavaScript exception immediately.

Issue #784 / PR #932 fixed strict postfix decrement through a `with` object
environment after the getter deleted the binding before writeback. Issue #785 /
PR #933 confirmed the same binding contract for strict postfix increment. Issue
#786 / PR #975 confirmed prefix decrement must use the same captured binding
writeback path. The generic property setter path could recreate the property,
but ECMAScript strict object-environment `SetMutableBinding` must throw when the
binding has disappeared. The durable lesson is to model object-environment
writeback as a binding operation first and only use property setting after the
strict missing-binding check has passed.

Issue #2880 / PR #2890 confirmed that the same rule applies before taking the
flat-slot numeric fast path for identifier update expressions. With dynamic
object environments active, the runner must resolve the current
assignment-reference target instead of trusting a previously stamped flat slot;
otherwise `with` bindings, unscopables, and strict missing-binding writeback can
be bypassed by `++`/`--`.

Issue #774 / PR #950 extended that lesson to plain assignment. The RHS can
delete the resolved `with` binding before `PutValue`; strict mode still has to
throw through the captured object-environment reference after RHS side effects,
not fall back to a generic identifier/property assignment path.

Issue #777 confirmed the same object-environment writeback contract for
compound assignment. The compound operator may read through a captured `with`
binding whose getter deletes the property before the final `PutValue`; strict
mode must still re-check the captured object binding and throw `ReferenceError`
instead of letting the generic setter recreate the property per operator.

Issue #829 / PR #1126 fixed simple IR `var` declarations with initializers.
The initializer can delete or otherwise mutate the `with` object after
`ResolveBinding` should already have selected the write target. The durable
lesson is that declaration evaluation has the same observable target-resolution
step: capture the reference before initializer evaluation and write through it
afterward.

Issue #772 / PR #947 fixed object destructuring `var` binding order for a
computed source property under `with`. The durable lesson is that destructuring
binding target resolution is observable and must occur after computed source-key
evaluation but before source getter/default side effects. The runner must keep
that in the compiled binding path and write through captured object-environment
references.

Issue #1070 / PR #1235 fixed Test262 `Statements_variable_dstr` crashes after
nested/default variable declaration binding raised `ThrowSignal` from
`ApplyBindingTargetProgram`. That signal represents a JavaScript throw
completion, not a host crash. The durable lesson is that declaration handlers
must convert binding-program throws back into `EvaluationContext` before using
the existing declaration throw slow path.

Issue #1063 / PR #1303 added `Statements_class_dstr` closeout coverage for
class-method destructuring. Review found the iterator-close regression was not
strong enough until it asserted the caught `Error|boom` and a single
`return()` call together. The durable lesson is that abrupt destructuring
proofs must verify both completion preservation and iterator cleanup, because
either half can pass while the other half regresses.

Issue #1753 / PR #1795 fixed `Statements_try_dstr` crashes in catch-parameter
array destructuring. `ApplyBindingTargetProgram` correctly reported the default
initializer throw through `EvaluationContext`, but the catch slow path treated
the throw as if it could advance to the catch body's next instruction. The
durable lesson is that catch-binding throws replace the caught value while still
needing the active catch/try frame to unwind through ordinary IR abrupt
completion, including IteratorClose, before an outer catch observes the
replacement error.

Issue #2054 / PR #2070 added direct slot writes for analyzer/lowering-proven
destructuring binding targets. The build-back repairs showed two separate
semantic edges: catch target programs must be stamped with the catch scope
active, and assignment-mode direct slot writes must fall back for immutable
named function expression self-bindings and global constants. The durable
lesson is that destructuring slot metadata is a provenance optimization only;
the runner must still revalidate runtime slot identity and preserve every
generic binding semantic not explicitly proven safe for the fast path.

Issue #778 / PR #970 fixed `delete super[expr]` ordering in expression
bytecode. Before `super()` initializes a derived constructor's `this`, the
`super` reference check must throw before the computed property key can run.
After initialization, the key may run, but `delete super[...]` still throws
`ReferenceError`. The durable lesson is to keep super-reference validation,
computed-key evaluation, and the operation-specific throw as separate ordered
steps.

Issue #1751 / PR #1790 added descriptor-backed delete regressions after the
compliance-gap run reported crashes in strict/sloppy delete-expression cases.
The durable lesson is that ordinary JavaScript delete must honor descriptor
configurability first, then let strictness decide whether a failed delete is a
JavaScript `TypeError` or a sloppy `false` completion. Internal force-delete
helpers are not ordinary delete semantics.

Issue #1752 / PR #1791 fixed Test262
`computed-reference-null-or-undefined.js` after ordinary expression bytecode
computed reads on `null` or `undefined` could crash the host instead of
throwing a JavaScript `TypeError`. The durable lesson is that ordinary
`base[key]` and optional `base?.[key]` have different nullish-base ordering:
ordinary reads evaluate the key expression before the nullish-base `TypeError`
but must not convert the key afterward, while optional reads skip the key
entirely when the base short-circuits.

Issue #1829 fixed compound indexed assignment ordering for nullish bases. The
durable lesson is that `base[key] op= rhs` has the same observable key/base
boundary as computed member access, but the bytecode stack shape is different:
the compiler must check the base at the correct stack depth before
`ToPropertyKey`, then reuse the resolved key for both the old-value read and
final writeback.

Issue #2880 / PR #2890 confirmed the same nullish-base/property-key boundary for
computed update expressions such as `base[key]++`. The ordinary computed key
expression is evaluated, but `ToPropertyKey` side effects must not run after a
nullish base has already selected the `TypeError` completion.

Issue #806 / PR #999 fixed the `Intl.NumberFormat`
`constructor-locales-hasproperty` fixture after `Array.prototype.push` stored
its recursion marker as `__inPush__` on the same JavaScript array used to record
proxy `HasProperty` lookups. The marker polluted later enumeration. The durable
lesson is that native guard state must stay hidden; otherwise guard bookkeeping
becomes a spec-visible property access side effect.

Issue #811 / PR #1007 added focused `Object.assign` regressions after the
issue-supplied Test262 `Object_assign` group was already green but lacked a
local guard for integrity-level accessor targets. The durable lesson is that
`Object.assign` must remain a throwing `Set` operation on the existing target:
integrity-level data-property restrictions do not block an existing setter, and
the same contract applies to Symbol keys.

Issue `autrun-diqz1xnc6eww-de7b218dd3` / PR #1689 migrated `Array.of`,
`Array.from`, iterable `Array.from`, and `Array.prototype.concat` callsites that
already held `JsValue` values away from the legacy `object?`
`CreateDataPropertyOrThrow` path. The durable lesson is that data-property
creation should keep typed JavaScript values typed when an equivalent
`JsValue` helper exists, while leaving genuinely host-primitive callsites for
separate, intentional slices.

Issue `autrun-diqzio3j4ge0-256eba07a6` / PR #1693 applied that intentional
slice to Intl result-object builders across `DisplayNames`, `DurationFormat`,
`ListFormat`, `PluralRules`, `RelativeTimeFormat`, and `Segmenter`. The durable
lesson is that once a result-builder slice is chosen, even raw host strings,
numbers, and booleans should enter the typed helper as explicit `JsValue`
instances so the object-building path no longer falls back through the legacy
`object?` carrier.

Issue `autrun-dir2jazax9jk-d413d0e4d7` / PR #1713 completed the array
data-property helper slice by deleting the now-dead
`CreateDataPropertyOrThrow(..., object? value, ...)` overload. Earlier guidance
allowed leaving the broader object helper for unmigrated callsites, but the
array cluster no longer had any such callers. Future data-property helper work
should treat a clean focused search as permission to remove the dead overload
and keep only the typed `JsValue` path.

Issue `autrun-dit384rttcps-5aec2213ed` / PR #2247 deduplicated the repeated
move-or-delete blocks in `Array.prototype.shift`, `unshift`, and `splice`.
The safe extraction was deliberately narrower than the surrounding loops:
the helper owns only the identical source-check, target-set, target-exists, and
delete-or-throw sequence. The durable lesson is that code reduction in sparse
Array mutators must preserve hole/deletion observability and leave each
mutator's range, direction, length, and insertion semantics explicit.
