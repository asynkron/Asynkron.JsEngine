# ADR 0286: Accept unified bytecode construct calls, keep super calls activation-gated

## Status

Accepted

## Context

Issue #2690 (Batch 3 of the unified-bytecode production call widening that began
with #2676 spread calls and #2689 optional calls) widens production
unified-bytecode routing to admit **synchronous non-spread construct calls** —
`new F(...)` — and asks whether the **super call family** (`super(...)`,
`LoadNamedSuperCallTarget`, `LoadComputedSuperCallTarget`) can join them.

Before this slice, the `TryFindExpressionDecline` arm declined all four
construct/super expression ops together:

```csharp
case ExpressionOpKind.Construct:
case ExpressionOpKind.SuperConstruct:
case ExpressionOpKind.LoadNamedSuperCallTarget:
case ExpressionOpKind.LoadComputedSuperCallTarget:
    declineCode = CallDependency;
    declineReason = "Construct and super call semantics are not eligible ...";
    return true;
```

This ADR records two distinct decisions: **accept `Construct`**, and **keep the
super call family declined** for a structural reason, not a cosmetic one.

### Why `new F(...)` is a clean, provable boundary

A construct expression program for `new F(a, b)` is laid out as
`[<constructor value>, <arg0>, .. <arg(n-1)>, Construct]`. Unlike `Call`, the
construct target carries **no receiver/`this`** — the constructor is pushed as an
ordinary value load and the `Construct` op consumes `argumentCount + 1` stack
slots, producing one result. This means construct reuses the existing
simple-operand lowering verbatim: the constructor and each argument are emitted
by their own preceding ops (slot loads, dynamic-identifier loads, literals), and
a single new boundary opcode performs `[[Construct]]`.

The observable behaviors preserved exactly, mirroring the spec-conformant
construct reference helper (`ExecuteProgramConstruct` /
`ExecuteProgramConstructNoSpread`):

- **`new.target` propagation**: `new F()` invokes `[[Construct]]` with `F` itself
  as `new.target`. The boundary passes the constructor callable as both target
  and new-target.
- **Evaluation order**: the constructor value and all argument loads are
  evaluated left-to-right before `[[Construct]]` is invoked.
- **Not-a-constructor**: a non-constructor target throws `TypeError`
  ("Target is not a constructor") at the boundary, matching the reference.
- **Result binding**: the construct result replaces the constructor slot on the
  operand stack.

### Why the super call family stays declined

`super(...)` and super-member call targets only ever appear inside **derived
class constructors**. Those activations are already declined *before* expression
eligibility runs, by the activation gate in
`SyncFunctionInvoker.CanUseProductionUnifiedBytecode`:

- `IsClassConstructor`
- `_function.IsDefaultDerivedConstructor`
- `_superConstructor is not null` / `_superPrototype is not null`
- `_lexicalThisEnvironment is not null`
- `!_instanceFields.IsDefaultOrEmpty`
- `!newTarget.IsUndefined` (the constructor is itself invoked via `[[Construct]]`)

Any function able to contain `super(...)` trips at least one of these gates, so a
`SuperConstruct` op can never reach `TryFindExpressionDecline` in a function that
the activation layer would route through production unified bytecode.

`ExecuteProgramSuperConstruct` is also deeply environment-dependent: it resolves
the super binding, walks the lexical-`this` environment, enforces the
"`super` may only be called once" `ThisInitialized` guard, re-binds `Symbol.This`
/ `Symbol.Super`, calls `MarkThisInitialized`, and runs pending class-field
initializers. Re-implementing ~170 lines of that machinery in the flat-slot VM —
to satisfy a code path the activation gate makes unreachable — would be
**untestable, unprovable dead code**, which contradicts the proof-pack
requirement that every admitted shape be demonstrable.

## Decision

1. **Admit `Construct`** for the non-spread sub-shape. Add a
   `ConstructInvocationBoundary` opcode; the compiler emits it for `Construct`
   expression ops, and the VM executes it by invoking `[[Construct]]` with the
   constructor as `new.target`, mirroring the construct reference helper. No
   fallback into the AST / generic expression-plan runner.

2. **Keep spread-onto-construct declined.** `new F(...args)` declines with
   `ObjectLiteralOrSpreadDependency` — spread flattening for construct is not yet
   modeled at the invocation boundary.

3. **Keep the super call family declined** (`SuperConstruct`,
   `LoadNamedSuperCallTarget`, `LoadComputedSuperCallTarget`) with
   `SuperPropertyDependency` and an explicit decline reason. They are unreachable
   in production (activation-gated) and admitting them would be unprovable dead
   code.

4. **Member-target constructs** (`new a.b()`) and **non-simple argument
   constructs** (`new F(g())`) remain declined: their receiver/argument chains
   fall outside the admitted simple-operand construct boundary, exactly as the
   equivalent call shapes do.

## Consequences

- `new F(...)` with an activation-resolved or dynamic-lookup constructor and
  simple operands now runs entirely on the unified-bytecode VM, gaining the same
  routing as Batch 1 calls.
- The `Construct` decline arm is gone; the super arm is preserved and tightened
  to a super-specific decline code. One existing eligibility theory case
  (`new ctor(value)` expecting decline) is removed because that shape is now
  admitted and covered by a positive eligibility test.
- Proof pack: `UnifiedBytecodeProductionConstructCallTests` covers `new.target`
  propagation, zero/many-arg construct, argument order, not-a-constructor
  `TypeError`, and the spread/member-target/super negative declines;
  `UnifiedBytecodeProductionEligibilityTests` proves admission of the construct
  boundary and the spread/member/super declines.
- The async/generator resumable route still rejects `ConstructInvocationBoundary`
  via the resumable opcode allowlist, so construct stays sync-only.

## Related

- Parent: #2676 (Batch 1 — spread calls, ADR 0287)
- Batch 2: optional calls (#2689)
- Precedent ADRs: 0261, 0262, 0263, 0264, 0275, 0287
- [Unified bytecode expansion contract](../unified-bytecode-expansion-contract.md)
