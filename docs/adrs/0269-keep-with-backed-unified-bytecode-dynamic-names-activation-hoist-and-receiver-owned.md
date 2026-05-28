# ADR 0269: Keep with-backed unified bytecode dynamic names activation-hoist and receiver-owned

## Status

Accepted

## Context

Issue #2564 / PR #2571 repaired the Test262
`Statements_with("language/statements/with/S12.10_A1.11_T5.js", False)` row
after the production unified bytecode with-backed dynamic-name path regressed
non-strict `with` semantics.

The failing shape called a nested function from inside an outer `with` object.
Inside that nested function, `throw value; var value = "local";` must resolve
`value` to the function's hoisted var binding and throw `undefined`; the outer
`with ({ value: ... })` object must not provide the value, and the name must not
fall through to a `ReferenceError`. The production route created a fast simple
activation environment, but that path had not installed the function-scoped var
bindings that full function declaration instantiation makes visible before body
execution.

The same production lane also owns receiver-aware identifier calls inside
`with`. A dynamic identifier call target such as `with (scope) { finish(); }`
must bind `this` to the with binding object when the identifier is provided by
that object. That receiver decision cannot be skipped just because identifier
caching is enabled for the surrounding context.

The accepted fix kept the with-backed production unified bytecode route. It did
not decline the shape or add a mixed fallback. Instead it aligned the fast
activation environment with hoisted function-scoped bindings and made the VM's
dynamic identifier call-target preparation consult active with bindings before
normal identifier lookup.

## Decision

Production unified bytecode may keep executing with-backed dynamic-name
programs, but the bridge must preserve both activation hoisting and with receiver
semantics.

- Before VM execution, every sync fast activation environment used for accepted
  production unified bytecode must define the same function-scoped var bindings
  that can shadow outer dynamic object environments. Non-parameter var-declared
  names are initialized to `undefined`, and a function-name binding that is also
  var-declared must be created when the function-name environment would
  otherwise hide it.
- Dynamic identifier lookup remains owned by the active `JsEnvironment` chain.
  `UnifiedBytecodeVirtualMachine.PrepareDynamicIdentifierCallTarget` must try a
  with binding whenever the environment chain contains a with object, regardless
  of `EvaluationContext.AllowIdentifierCache`.
- When a with binding is found, the call target pushes the binding object as the
  receiver and reads the callee through that captured with binding. Only the
  no-active-with path may use normal cached identifier lookup or the after-with
  miss helper.
- Do not repair this class of bug by disabling the production unified bytecode
  route, by adding callbacks into `ExpressionProgram`, `ExecutionPlanRunner`, or
  AST evaluation, or by broadening dynamic support to direct eval, captured
  dynamic activation, arguments objects, async functions, or generators.

## Consequences

- Future with-backed dynamic-name work must prove local var hoisting before
  outer with lookup, not only object-property visibility.
- Receiver-aware dynamic identifier calls need route-level coverage that proves
  both the production fast-path log and the `with` object as `this`.
- Activation fast-path edits that reuse simple IR environments for production
  unified bytecode must recheck function-scoped var hoisting, including
  parameter-shadowed names and function-name environment collisions.
- The current focused proof shape is the exact Release Test262 row plus
  `UnifiedBytecodeProductionInvocationTests` coverage for nested function var
  hoisting and with-object dynamic identifier calls.

## Related

- Issue #2564 / PR #2571
- ADR 0012: `docs/adrs/0012-keep-expression-bytecode-call-target-semantics-split.md`
- ADR 0052: `docs/adrs/0052-keep-dynamic-with-scope-cleanup-boundaries-identity-based.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- `docs/unified-bytecode-expansion-contract.md`
- `.claude/rules/unified-bytecode-prototypes.md`
