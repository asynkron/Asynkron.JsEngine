# ADR 0134: Keep simple declaration name inference runner-owned

## Status

Accepted

## Context

Issue #1840 / PR #1875 fixed the Test262 `Statements_let` rows for anonymous
function-name inference in simple `let` declarations. The affected rows covered
arrow, cover grammar, ordinary function, and generator initializer forms, plus a
related function-local TDZ closure row.

The declaration emitter already preserved the source intent by setting
`SimpleVariableDeclarationInstruction.AllowNameInference` for anonymous
function or class initializer shapes. The runtime declaration handler also
entered `EvaluationContext.CurrentFunctionNameHint` before evaluating the
initializer. That was sufficient for AST-created functions and some bytecode
literal paths, but expression-program lowering did not propagate the name hint
through every cover form that can still produce an anonymous
`IFunctionNameTarget`.

The tempting fix would have been to broaden expression bytecode helper semantics
or make every function/class literal loader responsible for recovering the
declaration target name. That would have coupled declaration-specific
`NamedEvaluation` semantics to generic expression execution and risked changing
assignment, property, object literal, and class expression paths.

## Decision

Keep simple declaration name inference owned by the IR declaration runner.

`HandleSimpleVariableDeclaration` should continue to:

1. create the lexical TDZ binding before initializer evaluation;
2. evaluate the initializer under the declaration's function-name hint;
3. handle throw, return, yield, and pending await before binding write; and
4. after successful initializer evaluation, if `AllowNameInference` is set and
   the resulting value is an `IFunctionNameTarget`, call
   `EnsureHasName(bindingName)` immediately before final declaration binding
   initialization or assignment.

This preserves declaration-specific anonymous function/class name inference
without broadening shared expression bytecode helpers or assignment/property
name-inference paths.

## Consequences

- Declaration `NamedEvaluation` remains local to
  `SimpleVariableDeclarationInstruction` execution, where the binding name,
  TDZ state, var/lexical kind, await state, and abrupt-completion path are all
  available together.
- Expression-program lowering can continue to normalize cover forms without
  needing every lowered function/class literal shape to duplicate declaration
  binding-name recovery.
- Future fixes for declaration name inference should not widen shared
  `EnsureHasName` helpers until exact declaration and non-declaration proof
  shows the broader path is required.
- Focused proof should start with the exact Test262 declaration rows or
  `Name=Statements_let`, then widen only if another declaration family fails.
- TDZ semantics stay separate: the declaration handler still creates
  `JsValue.Uninitialized` for lexical bindings before evaluating the
  initializer and only publishes the final value after successful evaluation.

## Related

- `.claude/rules/ecmascript-binding-name-inference.md`
- `.claude/rules/expression-bytecode-assignment.md`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Handlers.Declarations.cs`
