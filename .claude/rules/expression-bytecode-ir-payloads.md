# Expression Bytecode IR Payload Guardrails

When migrating expression-bearing IR instructions away from executable AST
payloads, prove the bytecode payload contract at the instruction level.

## Rules

1. Add or extend plan-walking tests over representative
   `ExecutionPlan.Instructions` when an instruction family starts carrying an
   `ExpressionProgram`, awaited `ExpressionProgram`, or equivalent bytecode
   payload.
2. Assert that each migrated expression-bearing instruction exposes the expected
   bytecode payload and that the payload is non-empty. Do not rely only on final
   runtime behavior; a runtime pass can hide a reintroduced AST payload.
3. If a migrated instruction keeps an AST compatibility property such as
   `ReturnExpression`, `Expression`, `Initializer`, `ValueExpression`, or
   `SourceExpression`, assert that the property remains null in the guardrail
   test.
4. Cover representative statement and control-flow families, not only the
   easiest expression statement shape. Include return, throw, branch,
   declaration, assignment, logical compound assignment, compound assignment,
   awaited assignment variants, await-and-discard, yield, `yield*`, `with`,
   loop initialization, and destructuring surfaces when the migration touches
   those areas.
5. Keep fixture assertions intent-based. Prefer finding the relevant
   instruction type and checking its payload over depending on fragile full
   instruction ordering.
6. For binding-target program guardrails, inspect nested subprograms as well as
   the top-level initializer/source program. Computed object binding names,
   default values, and similar nested `BindingTargetProgram` payloads must be
   non-empty bytecode programs when the migrated shape depends on them.
7. When producing statement-bytecode or IR payload classification tables from
   `InstructionKind`, mechanically check the table against the current enum and
   include terminal control-flow kinds such as `Throw` and `Return`. These
   kinds can look like ordinary control flow in family summaries, but their
   optional expression and await payloads require explicit expression-reference
   and async-resume normalization notes.
8. For terminal return/throw await work, prove direct await and nested await as
   separate payload contracts. Direct `return await value` and
   `throw await value` should stay on the instruction `AwaitedProgram` path with
   an `AwaitStateKey`; nested await inside a larger return/throw expression
   should be normalized through a synthetic awaited temp and then finish with a
   bytecode-backed `ReturnProgram` or `ThrowProgram`. Do not add
   `AwaitExpression` expression-bytecode ops or normalize direct awaits into
   synthetic temps just to make the tests uniform.
9. When a migrated instruction's AST compatibility property becomes production
   dead, delete the property instead of keeping a null-returning test hook.
   Update tests to assert the positive bytecode contract instead: the intended
   `InstructionKind`, non-empty `ExpressionProgram` or awaited program, and
   absence of broad fallback instruction shapes where relevant. Do not re-add
   an AST payload property only so tests can assert `null`.
10. For statement-level AST payload retirement, keep analysis-only AST references
   available until their owner analysis pass has consumed them, then retire them
   in the plan-lowering validation hook before publishing `ExecutionPlan`.
   `PushEnvironmentInstruction.SourceBlock` is the reference shape: slot
   analysis may read it, `LowerExpressionPayloads()` / validation must clear and
   reject any published instance that still carries it, and flat-slot mapping
   should stay separated from payload lowering.
11. When adding or updating instruction-payload audit ledgers, verify every
    listed instruction member name against the current record definitions before
    committing. Do not use plausible historical names in classification tables:
    for example, the return compatibility shim is
    `ReturnInstruction.ReturnExpression`, not `ReturnInstruction.Expression`.
12. When lowering awaited `if` conditions into synthetic awaited temporaries,
    guard the rewrite by JavaScript evaluation order, not only by "contains
    await". The guard must cover every expression shape the await rewriter can
    traverse. In particular, inspect `NewExpression.Constructor` as well as
    arguments, reject logical/nullish short-circuit shapes, and do not hoist
    awaits from `ConditionalExpression` branches into a pre-branch temp because
    that would eagerly evaluate branch-only work.
13. When adding negative tests for nested-await normalization boundaries, assert
    the preserved unsafe sub-shape rather than "no rewrite anywhere". A wrapper
    may still be validly lowered while the logical/nullish short-circuit,
    assignment-family value, or destructuring-default expression that owns the
    evaluation-order risk remains unnormalized.

## Why

Issue #1393 / PR #1397 expanded
`Issue400And722ExpressionBytecodeTraceabilityTests` after expression bytecode
coverage had outgrown single-shape checks. The risk was not a current runtime
failure; it was future IR work silently reintroducing executable AST payloads
while still passing behavior-level tests. Future migrations need explicit
instruction-payload guardrails so the non-dynamic fast path remains AST parse /
analyze -> IR emit -> bytecode execution, with AST evaluation quarantined to
intentional fallback seams only.

Issue #1439 / PR #1448 expanded the same guardrail after investigation found
representative instruction coverage still missed expression-bearing payload
families: logical compound assignment, compound assignment, awaited assignment
variants, await-and-discard, and nested object binding target programs. The
lesson is that a guardrail can be structurally correct but still too shallow if
it checks only the top-level instruction payload and skips suspending variants
or nested binding subprograms.

Issue #1470 / PR #1472 refined ADR-0094 after review caught that the concrete
statement-bytecode classification table omitted `Throw` and `Return`. Both
instructions are terminal control flow, but both also carry optional
expression/await payload fields. Future compact statement-bytecode planning
must prove enum coverage mechanically and classify payload shape, not only
control-flow role.

Issue #1485 / PR #1496 added focused lowering coverage for return/throw nested
await paths after the first high-value `UnsupportedExpressionProgram` slice was
selected from evaluation-order-safe statement contexts. The delivery found no
runtime change was needed, but it made the payload contract explicit: direct
await remains an awaited instruction payload, while nested await is rewritten
through a synthetic `__yield_lower_...` temp before the terminal instruction
uses ordinary expression bytecode. Future return/throw await work should keep
those proof shapes paired so direct fast paths are not accidentally replaced by
generic synthetic-temp normalization.

Issue #1492 / PR #1502 removed the dead
`EvaluateAndDiscardInstruction.Expression` compatibility property after the
first two statement-lowering slices had already moved sequence and conditional
expression statements onto dedicated IR instructions or expression bytecode.
The lesson is that a null AST-property assertion stops being a guardrail once
the production surface is dead. Future cleanup slices should preserve the live
instruction handlers, remove dead AST-shaped properties, and make tests prove
the bytecode payload and instruction-shape contract directly.

Issue #1490 / PR #1499 moved `PushEnvironmentInstruction.SourceBlock` retirement
from an ad hoc final-plan publication loop into the plan-lowering hook and added
validation that rejects a published `PushEnvironment` retaining `SourceBlock`.
The lesson is that statement AST payload retirement should have one invariant
point after required analysis has run. Future slices should not clear
analysis-owned payloads at emission time or hide the cleanup in unrelated
mapping/publication loops.

Issue #1489 / PR #1500 added an `ExecutionInstruction` AST-payload
classification ledger in `Instructions.cs`. Review found one ledger entry used
the plausible but wrong historical name `ReturnInstruction.Expression`; the
actual null compatibility shim is `ReturnInstruction.ReturnExpression`. Future
payload ledgers must be checked against the concrete record members, not only
the conceptual payload family, so the durable audit stays actionable when
agents use it for follow-up lowering/removal work.

Issue #1491 / PR #1501 lowered safe async `if` conditions into existing
`SimpleVariableDeclarationInstruction.AwaitedProgram` plus bytecode-backed
`BranchInstruction.ConditionProgram`. Review found that the safety guard must
mirror the rewriter traversal exactly: missing `NewExpression.Constructor`
would allow an unsafe short-circuit constructor hoist, and treating ternary
branches as generally rewritable would break branch-only evaluation. Future
branch-condition await work must prove direct and nested await branch payloads
while preserving short-circuit and conditional-expression evaluation order.

Issue #1512 / PR #1513 added focused negative coverage for the high-risk
normalization boundaries from the await-lowering handoff: nullish short-circuit
conditions, index-assignment values containing logical short-circuit await, and
destructuring assignment defaults containing logical short-circuit await. The
lesson is that these are evaluation-order guard tests, not broad "disable all
lowering" tests. Future coverage should prove the specific unsafe nested shape
survives while still allowing safe surrounding wrapper lowering.
