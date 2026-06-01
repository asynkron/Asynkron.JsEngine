# ADR 0319: Keep Map/Set fast dispatch method-identity marked

## Status

Accepted

## Context

Issue `autrun-dixf54wjiawg-2f739a705c` / PR #2946 optimized the `mapset`
profile by adding an IR plain-method fast path for calls such as
`map.set(key, value)`, `map.has(key)`, `set.add(value)`, and `set.has(value)`.
Focused CPU profiles showed repeated `InvokeCallableJsValueGeneric` and
`CastHelpers.Box` cost under `ExecutionPlanRunner.ExecuteProgramCall` for
native Map and Set prototype methods.

The initial optimization correctly stayed behind ordinary property lookup and
plain `JsMap` / `JsSet` receiver checks, but its callable guard matched native
host functions by display name. That was too broad. Map, Set, and WeakSet share
observable method names such as `has`, `delete`, and `clear`; user code can
assign one prototype's method onto another prototype. A display-name guard can
then run the wrong receiver-family storage operation instead of falling back to
the ordinary native method, which should reject an incompatible receiver with
`TypeError`.

The build-stage repair added an internal `MapSetFastMethodKind` marker on
engine-created `HostFunction` instances and stamped the specific Map and Set
prototype functions during prototype configuration. The IR fast path now
dispatches only when that marker matches the receiver family and method.

## Decision

Keep Map/Set IR plain-method fast dispatch marked by engine-owned method
identity, not by JavaScript-visible display names.

The accepted boundary is:

1. The receiver must still be a plain `JsMap` or `JsSet`.
2. The callable must be the engine-created host function stamped with the exact
   `MapSetFastMethodKind` for the receiver family and operation.
3. Shared display names such as `has`, `delete`, and `clear` are not semantic
   proof of method ownership.
4. User replacements, wrappers, cross-prototype method swaps, WeakSet methods,
   and unstamped host functions must stay on the ordinary invocation path.
5. The fast path may call owner storage helpers directly only after the marker
   and receiver-family checks have both passed.

## Consequences

- The retained `mapset` speedup avoids generic host-call argument-carrier
  materialization for the hot plain Map/Set method calls.
- Cross-prototype assignments such as
  `Map.prototype.has = Set.prototype.has` or
  `Set.prototype.clear = Map.prototype.clear` preserve ordinary built-in
  receiver validation and throw `TypeError`.
- Future collection-method fast paths must stamp and match exact built-in
  method identity. A method name, property path, or generic `HostFunction`
  check is not enough when multiple built-ins share names.
- Regression coverage should include both positive SameValueZero/override
  behavior and negative cross-family method replacement cases.

## Evidence

- Delivery PR #2946 merged as commit `345ca647e`.
- The performance note records the selected-profile baseline and final rows in
  `docs/performance/mapset-ir-plain-method-fast-path.md`.
- Build-stage repair commit `2cc39a3e2` changed:
  - `src/Asynkron.JsEngine/JsTypes/HostFunction.cs`
  - `src/Asynkron.JsEngine/StdLib/MapSet/MapPrototype.cs`
  - `src/Asynkron.JsEngine/StdLib/MapSet/SetPrototype.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Helpers.cs`
  - `tests/Asynkron.JsEngine.Tests/MapTests.cs`
  - `tests/Asynkron.JsEngine.Tests/SetTests.cs`
- Focused new tests, `MapTests|SetTests`, and `rtk git diff --check` passed in
  the build-stage verification summary.

## Related

- `docs/rules/host-function-observable-shape.md`
- `docs/rules/performance-profiling-guardrails.md`
- `docs/performance/mapset-ir-plain-method-fast-path.md`
- `docs/adrs/0227-keep-math-host-function-fast-dispatch-marked-and-arity-specific.md`
