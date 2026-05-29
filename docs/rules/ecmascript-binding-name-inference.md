# ECMAScript Binding Name Inference

When changing declaration, assignment, function/class literal, or expression
bytecode paths that can create anonymous functions or classes, keep ECMAScript
name inference tied to the syntax form that owns the binding name.

## Rules

1. For simple identifier declarations, treat
   `SimpleVariableDeclarationInstruction.AllowNameInference` as the declaration
   path's semantic signal. Evaluate the initializer under the current
   function-name hint, then after successful evaluation call
   `EnsureHasName(bindingName)` on an `IFunctionNameTarget` result before
   writing the binding.
2. Do not move declaration-specific name recovery into generic expression
   bytecode helpers unless focused proof shows every affected non-declaration
   path has the same ECMAScript `NamedEvaluation` semantics.
3. Keep declaration TDZ and name inference ordered separately: create the
   lexical binding as `JsValue.Uninitialized` before initializer evaluation, do
   not publish the final binding value until the initializer completes without
   throw, return, yield, or pending await, and apply name inference only to the
   successful initializer result.
4. Keep assignment and property-write name inference governed by their own
   rules. Identifier assignments, parenthesized assignments, member writes,
   object literal properties, and declaration initializers do not all share the
   same syntax exclusions.
5. Prove declaration-name changes with the exact fixture family first, such as
   the issue-listed `language/statements/let/fn-name-*` rows or
   `Name=Statements_let`, before widening into broader Test262 or runtime
   changes.

## Why

Issue #1840 / PR #1875 fixed `let` statement Test262 rows where anonymous
function, arrow, cover, and generator initializer forms failed to receive the
declaration binding name after expression-program lowering. The safe fix stayed
in `HandleSimpleVariableDeclaration`: after initializer evaluation succeeded,
the runner applied `EnsureHasName` to the resulting `IFunctionNameTarget` when
`AllowNameInference` was set. The durable lesson is that declaration
`NamedEvaluation` belongs at the declaration boundary, where binding name, TDZ
state, and abrupt-completion handling are all visible, rather than in broad
expression-bytecode or assignment helpers.

Related ADR: `docs/adrs/0134-keep-simple-declaration-name-inference-runner-owned.md`.
