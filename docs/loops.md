# Loop, Generator, and Async Architecture in JsEngine2

This document describes how JavaScript loops, generators, and async/await are implemented in JsEngine2, covering the evaluation pipeline, normalization layers, IR compilation, yield semantics, and async integration.

## Overview

The engine handles loops through a multi-layer architecture:

```
JavaScript Source
       ↓
   AST Nodes (4 loop statement types)
       ↓
   Normalization Layer (2 plan types)
       ↓
   ┌─────────────────┬──────────────────┐
   │  Direct Eval    │   IR Compiler    │
   │  (sync code)    │  (generators/    │
   │                 │   async)         │
   └─────────────────┴──────────────────┘
       ↓                    ↓
   CompletionSignals    Flat Instructions
   (dynamic control)    (PC-based control)
```

## AST Node Types

Four statement types represent all JavaScript loops:

### 1. ForStatement
```csharp
// src/Asynkron.JsEngine/Ast/Statements.cs
record ForStatement(
    StatementNode? Initializer,    // let i = 0
    ExpressionNode? Condition,     // i < 10
    ExpressionNode? Increment,     // i++
    StatementNode Body);
```

### 2. WhileStatement
```csharp
record WhileStatement(
    ExpressionNode Condition,
    StatementNode Body);
```

### 3. DoWhileStatement
```csharp
record DoWhileStatement(
    StatementNode Body,
    ExpressionNode Condition);
```

### 4. ForEachStatement
```csharp
record ForEachStatement(
    BindingTarget Target,          // x or {a, b} or [a, b]
    ExpressionNode Iterable,
    StatementNode Body,
    ForEachKind Kind,              // In, Of, AwaitOf
    VariableKind? DeclarationKind); // let, const, var, or null
```

## Normalization Layer

### LoopPlan - Classic Loops Unified

For/while/do-while are normalized to a single `LoopPlan` representation:

```csharp
// src/Asynkron.JsEngine/Execution/LoopPlan.cs
record LoopPlan(
    LoopKind Kind,                              // While, DoWhile, For
    ImmutableArray<StatementNode> LeadingStatements,    // for-loop init
    ImmutableArray<StatementNode> ConditionPrologue,
    ExpressionNode Condition,
    BlockStatement Body,
    ImmutableArray<StatementNode> PostIteration,        // for-loop increment
    bool ConditionAfterBody,                            // do-while vs while
    ImmutableArray<Symbol> PerIterationBindings,        // let/const in for
    bool AllowIterationEnvironmentPooling);
```

**Normalization examples:**

```javascript
// while loop
while (x < 10) { body }
// → LoopPlan(Condition=x<10, ConditionAfterBody=false, Body=body)

// do-while loop
do { body } while (x < 10)
// → LoopPlan(Condition=x<10, ConditionAfterBody=true, Body=body)

// for loop
for (let i = 0; i < 10; i++) { body }
// → LoopPlan(
//     LeadingStatements=[let i = 0],
//     Condition=i<10,
//     PostIteration=[i++],
//     PerIterationBindings=[i],
//     Body=body)
```

### IteratorDriverPlan - Iterator Loops

For-in/for-of/for-await-of use a separate plan:

```csharp
// src/Asynkron.JsEngine/Execution/IteratorDriver.cs
record IteratorDriverPlan(
    IteratorDriverKind Kind,       // Sync or Await
    ExpressionNode Iterable,
    BindingTarget Target,
    VariableKind? DeclarationKind,
    BlockStatement Body);
```

### Why Two Plans?

| Aspect | LoopPlan | IteratorDriverPlan |
|--------|----------|-------------------|
| Termination | Boolean condition | Iterator exhaustion |
| Value source | N/A | Iterator `.next().value` |
| Cleanup | None | `IteratorClose` on break/throw |
| Protocol | None | Iterator protocol (@@iterator) |

The **IteratorClose requirement** is the key difference. When you `break` or `throw` out of a for-of loop, you MUST call `iterator.return()` if the iterator isn't done. Classic loops have no such cleanup.

**For-in** is special - it doesn't use iterator protocol but directly enumerates property keys via `EnumeratePropertyKeys`.

## Control Flow: CompletionSignals

For synchronous, non-generator code, control flow uses CompletionSignals:

```csharp
// src/Asynkron.JsEngine/ICompletionSignal.cs
interface ICompletionSignal;
record BreakCompletionSignal(Symbol? Label);
record ContinueCompletionSignal(Symbol? Label);
record ReturnCompletionSignal(JsValue Value);
record ThrowFlowCompletionSignal(JsValue Value);
record YieldCompletionSignal(JsValue Value, JsIteratorResultObject? ResultObject);
```

**Usage in loop evaluation:**

```csharp
// src/Asynkron.JsEngine/Ast/LoopPlanExtensions.cs (simplified)
while (true)
{
    if (!ConditionAfterBody && !EvaluateCondition())
        break;

    EvaluateBody();

    if (context.TryClearContinue(loopLabel))
        continue;  // Signal consumed, next iteration

    if (context.TryClearBreak(loopLabel))
        break;     // Signal consumed, exit loop

    if (context.IsReturn || context.IsThrow)
        break;     // Propagate up

    EvaluatePostIteration();

    if (ConditionAfterBody && !EvaluateCondition())
        break;
}
```

**Label matching:** Signals carry optional labels. `TryClearBreak(label)` only clears if labels match (or signal is unlabeled), otherwise the signal propagates to outer loops.

## IR Compilation: The Bytecode Layer

For generators and async functions, loops compile to a **flat instruction list** - essentially bytecode with rich instruction records instead of byte opcodes.

### Instruction Types

```csharp
// src/Asynkron.JsEngine/Execution/GeneratorIr.cs
abstract record GeneratorInstruction;

// Control flow
record BranchInstruction(ExpressionNode Condition, int ConsequentIndex, int AlternateIndex);
record JumpInstruction(int TargetIndex);
record BreakInstruction(int TargetIndex);
record ContinueInstruction(int TargetIndex);

// Generator-specific
record YieldInstruction(int ContinuationIndex, ExpressionNode? Expression);
record YieldStarInstruction(int ContinuationIndex, ExpressionNode Expression, Symbol StateSymbol, Symbol? ResultSlot);
record StoreResumeValueInstruction(int NextIndex, Symbol? TargetSlot);
record ReturnInstruction(ExpressionNode? Expression);

// Exception handling
record EnterTryInstruction(int TryEntry, int CatchEntry, Symbol? CatchSlot, int FinallyEntry);
record LeaveTryInstruction(int NextIndex);
record EndFinallyInstruction(int NextIndex);

// Iterator operations
record IteratorInitInstruction(ExpressionNode Iterable, IteratorDriverKind Kind, Symbol IteratorSlot, Symbol ValueSlot, int Next);
record IteratorMoveNextInstruction(Symbol IteratorSlot, Symbol ValueSlot, int BodyIndex, int BreakIndex);
record IteratorCloseInstruction(Symbol IteratorSlot, int NextIndex);

// Statements
record StatementInstruction(int NextIndex, StatementNode Statement);
```

### Comparison with Traditional Bytecode

| Aspect | Traditional Bytecode | JsEngine2 IR |
|--------|---------------------|--------------|
| Instructions | `0x4A` (opcode byte) | `BranchInstruction` record |
| Operands | Following bytes | Record properties |
| Dispatch | Switch on byte value | Switch on record type |
| Program counter | `int pc` | `int _programCounter` |
| Storage | `byte[]` | `ImmutableArray<GeneratorInstruction>` |
| Expressions | Compiled to bytecode | Embedded AST nodes |

**Trade-offs:**

JsEngine2 approach (rich records):
- Easier to debug and inspect
- Can embed AST nodes (expressions evaluated lazily)
- Type-safe dispatch
- More memory per instruction
- Slower dispatch than jump tables

Traditional bytecode:
- Compact (cache-friendly)
- Fast dispatch (computed goto)
- Serializable
- Needs separate constant pool
- Harder to debug

### IR Builder: Backward Construction

The `SyncGeneratorIrBuilder` constructs IR **backwards** - it builds from the end to know jump targets:

```csharp
// src/Asynkron.JsEngine/Execution/SyncGeneratorIrBuilder.cs
private bool TryBuildStatementList(ImmutableArray<StatementNode> statements, int nextIndex, out int entryIndex)
{
    var currentNext = nextIndex;
    for (var i = statements.Length - 1; i >= 0; i--)  // Reverse order!
    {
        TryBuildStatement(statements[i], currentNext, out currentNext);
    }
    entryIndex = currentNext;
}
```

When building instruction N, you already know instruction N+1's index.

### Nested Control Flow Flattening

**If statements** become branch instructions:

```javascript
if (x) {
  yield 1;
} else {
  yield 2;
}
```

Becomes:
```
[0] BranchInstruction(x, consequent=1, alternate=3)
[1] YieldInstruction(1)
[2] StoreResumeValue → goto 5
[3] YieldInstruction(2)
[4] StoreResumeValue → goto 5
[5] (next statement)
```

**Nested loops** use a scope stack for break/continue targets:

```javascript
outer: for (let i = 0; i < 3; i++) {
  for (let j = 0; j < 3; j++) {
    if (j === 1) continue;
    if (i === 1) break outer;
    yield i * j;
  }
}
```

Becomes:
```
[0]  StatementInstruction(let i = 0)           // outer init
[1]  JumpInstruction → 2
[2]  BranchInstruction(i < 3, then=3, else=18) // outer condition
[3]  StatementInstruction(let j = 0)           // inner init
[4]  JumpInstruction → 5
[5]  BranchInstruction(j < 3, then=6, else=15) // inner condition
[6]  BranchInstruction(j === 1, then=7, else=8)
[7]  ContinueInstruction → 13                  // continue (inner)
[8]  BranchInstruction(i === 1, then=9, else=10)
[9]  BreakInstruction → 18                     // break outer
[10] YieldInstruction(i * j)
[11] StoreResumeValue → 12
[12] JumpInstruction → 13
[13] StatementInstruction(j++)                 // inner post-iteration
[14] JumpInstruction → 5                       // back to inner condition
[15] StatementInstruction(i++)                 // outer post-iteration
[16] JumpInstruction → 2                       // back to outer condition
[17] (implicit return)
[18] (after outer loop)
```

**Switch statements** are lowered to if/else chains before IR building:

```javascript
switch (x) {
  case 1: yield 'a'; break;
  case 2: yield 'b'; break;
}
```

Transforms to:
```javascript
{
  const __disc = x;
  let __match = -1;
  let __done = false;
  if (__match === -1 && __disc === 1) __match = 0;
  if (__match === -1 && __disc === 2) __match = 1;
  if (!__done && __match !== -1 && __match <= 0) { yield 'a'; __done = true; }
  if (!__done && __match !== -1 && __match <= 1) { yield 'b'; __done = true; }
}
```

Then compiles to branch instructions.

**Try/catch/finally** becomes explicit enter/leave instructions:

```
[0] EnterTryInstruction(tryEntry=1, catchEntry=4, finallyEntry=7)
[1] ... try body ...
[2] LeaveTryInstruction → 10
[3] (unreachable)
[4] ... catch body ...
[5] LeaveTryInstruction → 10
[6] (unreachable)
[7] ... finally body ...
[8] EndFinallyInstruction → 10
[9] (unreachable)
[10] (after try)
```

### IR Execution

The interpreter is a simple PC-based loop:

```csharp
// src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedGeneratorInstance.cs (simplified)
while (true)
{
    var instruction = _instructions[_programCounter];
    switch (instruction)
    {
        case BranchInstruction b:
            var result = EvaluateCondition(b.Condition);
            _programCounter = result ? b.ConsequentIndex : b.AlternateIndex;
            break;

        case JumpInstruction j:
            _programCounter = j.TargetIndex;
            break;

        case BreakInstruction br:
            if (HandleAbruptCompletion(AbruptKind.Break, br.TargetIndex))
                continue;  // Handle pending finally
            _programCounter = br.TargetIndex;
            break;

        case YieldInstruction y:
            var value = EvaluateExpression(y.Expression);
            _programCounter = y.ContinuationIndex;
            return new YieldResult(value);

        // ... etc
    }
}
```

## Yield and Generators

Generators are the primary reason for the IR layer. The `yield` keyword creates suspension points that require the flat instruction model.

### Yield Instructions

```csharp
// Simple yield: yield <expression>
record YieldInstruction(int ContinuationIndex, ExpressionNode? Expression);

// Store the value passed to .next(value) after resume
record StoreResumeValueInstruction(int NextIndex, Symbol? TargetSlot);

// Delegated yield: yield* <iterable>
record YieldStarInstruction(int ContinuationIndex, ExpressionNode Expression, Symbol StateSymbol, Symbol? ResultSlot);
```

### How Yield Works

A simple `yield` compiles to two instructions:

```javascript
function* gen() {
  const x = yield 1;
  yield x + 1;
}
```

Becomes:
```
[0] YieldInstruction(continuationIndex=1, expression=1)
[1] StoreResumeValueInstruction(nextIndex=2, targetSlot=x)
[2] YieldInstruction(continuationIndex=3, expression=x+1)
[3] StoreResumeValueInstruction(nextIndex=4, targetSlot=null)
[4] ReturnInstruction(undefined)
```

**Execution flow:**

1. `gen.next()` starts at PC=0
2. `YieldInstruction` evaluates expression (1), sets PC=1, returns `{value: 1, done: false}`
3. Generator suspends
4. `gen.next(42)` resumes at PC=1
5. `StoreResumeValueInstruction` stores 42 into slot `x`, sets PC=2
6. `YieldInstruction` evaluates `x+1` (43), sets PC=3, returns `{value: 43, done: false}`
7. Generator suspends
8. `gen.next()` resumes at PC=3
9. `StoreResumeValueInstruction` (no slot), sets PC=4
10. `ReturnInstruction` returns `{value: undefined, done: true}`

### Yield in Loops

Yield inside loops is where the flat IR shines:

```javascript
function* range(n) {
  for (let i = 0; i < n; i++) {
    yield i;
  }
}
```

Becomes:
```
[0] StatementInstruction(let i = 0)
[1] JumpInstruction → 2
[2] BranchInstruction(i < n, then=3, else=7)
[3] YieldInstruction(continuationIndex=4, expression=i)
[4] StoreResumeValueInstruction(nextIndex=5, targetSlot=null)
[5] StatementInstruction(i++)
[6] JumpInstruction → 2
[7] ReturnInstruction(undefined)
```

Each `.next()` call:
1. Resumes at saved PC
2. Executes until next `YieldInstruction`
3. Saves PC to continuation index
4. Returns yielded value

The loop state (variable `i`) lives in the environment, which persists across suspensions.

### Yield* Delegation

`yield*` delegates to another iterable, forwarding all values:

```javascript
function* concat(a, b) {
  yield* a;
  yield* b;
}
```

The `YieldStarInstruction` handles the full delegation protocol:

1. Get iterator from iterable
2. Loop: call `iterator.next()`
3. If not done, yield the value and suspend
4. On resume, pass received value to next `iterator.next(value)`
5. When done, store final return value (if `ResultSlot` specified)

This is complex because:
- Must handle `.throw()` and `.return()` being called on outer generator
- Must forward these to inner iterator if it supports them
- Must handle inner iterator throwing

### Generator States

```csharp
internal enum GeneratorState
{
    SuspendedStart,    // Created but never started
    SuspendedYield,    // Suspended at a yield point
    Executing,         // Currently running
    Completed          // Finished (returned or threw)
}
```

State transitions:
```
SuspendedStart ──.next()──→ Executing ──yield──→ SuspendedYield
                                ↓                      ↓
                             return              .next()/.throw()
                                ↓                      ↓
                            Completed ←────────── Executing
```

### Resume Modes

When resuming a generator, there are three modes:

```csharp
internal enum ResumeMode
{
    Next,   // .next(value) - normal continuation
    Throw,  // .throw(error) - throw at suspension point
    Return  // .return(value) - force return
}
```

The IR interpreter handles these:

```csharp
switch (resumeMode)
{
    case ResumeMode.Next:
        // Store value in resume slot, continue normally
        break;
    case ResumeMode.Throw:
        // Set throw signal, let exception handling take over
        break;
    case ResumeMode.Return:
        // Jump to finally blocks if any, then return
        break;
}
```

### Yield and Try/Finally

Yield inside try/finally requires special handling:

```javascript
function* gen() {
  try {
    yield 1;
    yield 2;
  } finally {
    yield 'cleanup';
  }
}
```

If caller calls `.return()` while suspended at `yield 1`:
1. Generator must execute the finally block
2. `yield 'cleanup'` suspends again
3. Only after finally completes does generator finish

The IR tracks this with `EnterTryInstruction` and pending completion state:

```csharp
// When .return() is called during yield
_pendingCompletion = new PendingCompletion(AbruptKind.Return, returnValue);
// Jump to finally entry, which will yield 'cleanup'
// After finally, EndFinallyInstruction checks _pendingCompletion
// and completes the return
```

## Async/Await and Loops

### No CPS Transformation

The engine does **NOT** use full Continuation-Passing Style transformation for async loops. Instead, it uses the same IR infrastructure with suspend/resume semantics.

### How Await Suspends

When an await in a loop hits a pending promise:

1. Save `_programCounter` (current instruction index)
2. Store loop state in `IteratorDriverState`:
   ```csharp
   internal sealed class IteratorDriverState
   {
       public IJsObjectLike? IteratorObject { get; init; }
       public bool AwaitingNextResult { get; set; }  // Waiting for .next()
       public bool AwaitingValue { get; set; }       // Waiting for value
       public IJsCallable? NextMethod { get; set; }
   }
   ```
3. Set generator to `Suspended` state
4. Return control to caller

### How Resume Works

When the promise resolves and the event loop continues:

1. Restore `_programCounter` (same instruction)
2. Check state flags (`AwaitingNextResult`, `AwaitingValue`)
3. Use resolved value and continue execution

```
First execution:          Resume:

PC=1 → MoveNext          PC=1 → MoveNext (same instruction!)
       ↓                        ↓
       await pending            check AwaitingNextResult flag
       ↓                        ↓
       SUSPEND                  use resolved value
       (save PC=1)              ↓
                               continue to body
```

### No Replay

There is NO replay mechanism. The loop doesn't re-execute from scratch:

- AST is NOT re-evaluated from a saved point
- Only the program counter needs to be saved
- State flags determine where to resume within an instruction
- This is a resumable state machine, not replay

### Async Generator Step Execution

Async generators wrap the IR interpreter with Promise-based stepping:

```csharp
// src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInstance.cs
var step = _inner.ExecuteAsyncStep(mode, argument);
switch (step.Kind)
{
    case AsyncGeneratorStepKind.Yield:
    case AsyncGeneratorStepKind.Completed:
        resolve(iteratorResult);
        break;
    case AsyncGeneratorStepKind.Pending:
        // Attach handlers to resume on promise settlement
        step.PendingPromise.OnFulfilled(value =>
            generator.Resume(ResumeMode.Next, value));
        step.PendingPromise.OnRejected(error =>
            generator.Resume(ResumeMode.Throw, error));
        break;
}
```

## Summary: Two Execution Models

| Aspect | Synchronous Code | Generators/Async |
|--------|-----------------|------------------|
| Representation | AST + LoopPlan/IteratorDriverPlan | Flat IR instructions |
| Control flow | CompletionSignals (dynamic) | PC updates (static targets) |
| Break/continue | Signals with label matching | Instructions with target index |
| Suspend/resume | N/A | Save/restore PC + state flags |
| Label resolution | Runtime | Compile-time |

**Key insight:** For async code, all loops ARE unified - they all compile to the same IR with the same suspend/resume mechanism. The complexity in separate evaluators is mainly for the synchronous path.

## File Reference

| Component | File |
|-----------|------|
| **AST Layer** | |
| AST nodes | `src/Asynkron.JsEngine/Ast/Statements.cs` |
| Yield expression | `src/Asynkron.JsEngine/Ast/Expressions.cs` |
| **Normalization** | |
| LoopPlan | `src/Asynkron.JsEngine/Execution/LoopPlan.cs` |
| LoopNormalizer | `src/Asynkron.JsEngine/Execution/LoopNormalizer.cs` |
| IteratorDriverPlan | `src/Asynkron.JsEngine/Execution/IteratorDriver.cs` |
| **Synchronous Evaluation** | |
| LoopPlan evaluator | `src/Asynkron.JsEngine/Ast/LoopPlanExtensions.cs` |
| Iterator evaluator | `src/Asynkron.JsEngine/Ast/IteratorDriverPlanExtensions.cs` |
| CompletionSignals | `src/Asynkron.JsEngine/ICompletionSignal.cs` |
| **IR/Bytecode Layer** | |
| IR instructions | `src/Asynkron.JsEngine/Execution/GeneratorIr.cs` |
| IR builder | `src/Asynkron.JsEngine/Execution/SyncGeneratorIrBuilder.cs` |
| Yield lowerer | `src/Asynkron.JsEngine/Execution/GeneratorYieldLowerer.cs` |
| **Generator Runtime** | |
| IR interpreter | `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedGeneratorInstance.cs` |
| Generator factory | `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedGeneratorFactory.cs` |
| **Async Runtime** | |
| Async generator | `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInstance.cs` |
| Async generator factory | `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorFactory.cs` |
| Await scheduler | `src/Asynkron.JsEngine/Ast/AwaitScheduler.cs` |
| Await expression | `src/Asynkron.JsEngine/Ast/AwaitExpressionExtensions.cs` |

## Future Considerations

### Potential Unification

The synchronous path could potentially use IR too, making IR the single unified loop representation. This would:

- Eliminate duplicate evaluation logic
- Provide consistent control flow handling
- Enable optimizations across all code paths

Trade-off: Added compilation overhead for simple synchronous code that doesn't need suspend/resume.

### Expression Compilation

Currently, expressions remain as AST nodes embedded in IR instructions. A "full" bytecode approach would compile expressions too:

```
// Current: expression in IR
BranchInstruction(Condition: BinaryExpression(x, <, 3), ...)

// Full bytecode would be:
LOAD_LOCAL 0      // x
PUSH_INT 3
LESS_THAN
BRANCH_IF_FALSE target
```

This would improve performance but increase complexity.
