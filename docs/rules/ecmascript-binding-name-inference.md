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
6. When admitting `DefineObjectProperty` or `DefineComputedObjectProperty` with
   `AllowNameInference` set to the unified bytecode production VM, handle the
   flag with a VM-side `EnsureHasName` call on the property value result —
   mirror the `StoreDynamicIdentifierValue` pattern. `EnsureHasName` is a safe
   no-op when the value is not an `IFunctionNameTarget`, so the call is correct
   for identifier-reference, non-reference, and function-literal values alike.
   Once `LoadFunctionLiteral` is in the allowed-opcode subset (not the decline
   block), the eligibility and compiler `AllowNameInference` guards on
   `DefineObjectProperty` and `DefineComputedObjectProperty` become dead code
   and must be removed; removing them must be paired with the VM `EnsureHasName`
   addition in the same slice. Note: `TryMeasureSimpleObjectLiteralSpan`
   (ADR 0290) keeps a separate `AllowNameInference` decline for the call-argument
   span-measurement context — that is an independent, narrower contract (simple
   non-name-inferring values only as arguments) and must not be conflated with
   the full production object literal return path. WHY: issue
   `planitem-planmanual1780157100924814000-baseline-batch-2-object-literal-shorthand-e0f8cc5711`
   / PR #2738 unblocked shorthand properties `{ a, b }` while `LoadFunctionLiteral`
   still declined; issue
   `planitem-planmanual1780157100924814000-baseline-batch-2-object-literal-shorthand-ebbe2ff1ae`
   / PR #2740 completed the picture by admitting `LoadFunctionLiteral` and
   confirming that the `AllowNameInference` eligibility/compiler guards are
   unreachable in both the shorthand and function-literal-valued property shapes.

7. When admitting any opcode to the unified bytecode allowed-opcode subset that
   can write a function-valued result to a slot or property, audit whether that
   opcode must perform name inference before the write. The audit covers three
   independent encoding schemes:
   - **`StoreSlot`** — `AllowNameInference` is encoded as the sign bit of the
     operand (`Operand < 0`); the actual slot index is recovered with
     `& 0x7FFFFFFF`. Both the run-loop and the step/debug-loop handlers must
     decode the flag and call `EnsureHasName` using
     `GetSlotName(program, slotIndex)` before writing to `slots[slotIndex]`.
   - **`StoreDynamicIdentifierReference`** — `AllowNameInference` is bit 0 of
     the operand; the string-constant index occupies bits 1+.
   - **`DefineObjectProperty` / `DefineComputedObjectProperty`** —
     `AllowNameInference` is bit 1, decoded via
     `DecodeDefineObjectPropertyAllowNameInference`.

   Do not assume that admitting a function-producing opcode (`LoadFunctionLiteral`)
   automatically causes downstream write opcodes to infer names. The write
   opcode must independently decode and act on its own flag. WHY: PR #2740
   admitted `LoadFunctionLiteral` to the unified bytecode production subset but
   did not extend `StoreSlot` to perform name inference; the regression —
   `assigned.name` remaining empty for `const f = function() {}` and shorthand
   object method patterns routed through `StoreSlot` — was discovered post-merge
   and fixed in PR #2737 (issue
   `planitem-planmanual1780157100924814000-baseline-batch-2-object-literal-shorthand-c8927602a4`).
   The root cause was that the opcode-expansion audit stopped at
   eligibility/compiler guards and did not check every downstream write site for
   name-inference applicability.

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
