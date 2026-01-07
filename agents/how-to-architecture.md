# Architecture Overview

This document describes the unusual and complex aspects of the Asynkron.JsEngine implementation.

## Execution Model Overview

The engine has two execution paths:

1. **AST Walking** - Direct recursive evaluation of the AST tree (sync functions with `with`/`eval`)
2. **IR Execution** - Lowered intermediate representation with a program counter (all other cases)

```
JavaScript Source
       |
   [Parser] -> AST
       |
   +-------------------------------------+
   |  IR-first: try ExecutionPlanBuilder |
   |  Fallback: AST Walking              |
   +-------------------------------------+
       |
   JsValue result
```

---

## AST

The AST is a typed representation of JavaScript syntax. Key types live in `Asynkron.JsEngine/Ast/`.

### AST Cache

**File:** `Ast/AstCache.cs`

AST nodes cache computed metadata to avoid repeated work. The pattern uses thread-safe lazy initialization:

```csharp
internal static TCache GetOrCreate<TCache>(ref TCache? field, Func<TCache> factory)
{
    var existing = Volatile.Read(ref field);
    if (existing is not null) return existing;

    var created = factory();
    var prior = Interlocked.CompareExchange(ref field, created, null);
    return prior ?? created;
}
```

**Usage:** AST nodes implement `IAstCacheable<T>` and store cached plans:
- `HoistPlan` - variable hoisting analysis
- `HoistableDeclarationsPlan` - function declarations to hoist
- `ExecutionPlan` - lowered IR for generators/async

**All usages of AST Cache:**
- `FunctionExpression` - caches `ExecutionPlan`, `HoistPlan`
- `BlockStatement` - caches `HoistPlan`, `HoistableDeclarationsPlan`
- `ProgramNode` - caches script-level plans
- Various statement nodes - cache scope analysis

### AST Walking Evaluation

**Files:** `Ast/*Extensions.cs`

Functions with `with` statements or direct `eval` use recursive evaluation. Each AST node type has extension methods:

```csharp
// Example: BlockStatementExtensions.cs
internal static JsValue EvaluateForJsValue(
    this BlockStatement block,
    ref JsEnvironment environment,
    EvaluationContext context)
{
    foreach (var statement in block.Body)
    {
        var result = statement.EvaluateForJsValue(ref environment, context);
        // handle completion signals...
    }
    return result;
}
```

### Completion Signals (Return Flow)

**File:** `CompletionSignals.cs`

Control flow (return, break, continue, throw, yield) is modeled as typed signals rather than exceptions or state machines:

```csharp
interface ICompletionSignal { }

record BreakCompletionSignal(Symbol? Label) : ICompletionSignal;
record ContinueCompletionSignal(Symbol? Label) : ICompletionSignal;
class ReturnCompletionSignal(JsValue value) : ICompletionSignal;
class ThrowFlowCompletionSignal(JsValue value) : ICompletionSignal;
class YieldCompletionSignal(JsValue value) : ICompletionSignal;
class PendingAwaitCompletionSignal : ICompletionSignal;
```

**Why signals instead of exceptions:**
- Signals are faster than throwing/catching exceptions
- They carry typed data (the return value, label, etc.)
- AST walkers check for signals after each statement and propagate them

**Pattern in evaluators:**
```csharp
var result = child.EvaluateForJsValue(ref env, ctx);
if (ctx.CompletionSignal is ReturnCompletionSignal ret)
    return ret.JsValue;
if (ctx.CompletionSignal is BreakCompletionSignal { Label: null })
    break;
```

---

## Intermediate Representation (IR)

The primary execution path. AST is lowered to a flat instruction sequence.

### Lowering

**Files:** `Execution/ExecutionPlanBuilder.cs`, `Execution/Emitters/*.cs`

Lowering transforms nested AST into a linear instruction stream with explicit jumps:

```
AST:                          IR:
if (x) {                      0: Branch(x, then=1, else=3)
  a();                        1: Statement(a())
} else {                      2: Jump(4)
  b();                        3: Statement(b())
}                             4: ...
```

**Emitters** handle specific AST constructs:
- `LoopEmitter` - for/while/do-while loops
- `ForOfEmitter` - for-of/for-in iteration
- `TryEmitter` - try/catch/finally
- `SwitchEmitter` - switch statements
- `BlockEmitter` - block scopes
- `YieldEmitter` - yield/yield*

### ExecutionPlan

**File:** `Execution/ExecutionPlan.cs`

The result of lowering. Contains:

```csharp
record ExecutionPlan(
    ImmutableArray<ExecutionInstruction> Instructions,
    int EntryPoint,
    int SlotCount,
    ImmutableArray<Symbol> SlotSymbols,
    int RootSlotCount,
    ImmutableDictionary<Symbol, int>? RootSlotMap,
    int FlatSlotCount,
    ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>>? FlatSlotMappings
);
```

### ExecutionInstruction

**File:** `Execution/Instructions/ExecutionInstruction.cs`

Base class for all IR instructions:

```csharp
abstract record ExecutionInstruction(InstructionKind Kind, int Next);
```

**InstructionKind** enum enables fast dispatch (jump table) instead of type pattern matching:

```csharp
enum InstructionKind : byte {
    Statement, Throw, EvaluateAndDiscard, BinaryOp,
    IncrementSlot, CompoundAssignmentSlot,
    PushEnvironment, PopEnvironment,
    Yield, YieldStar, StoreResumeValue,
    EnterTry, EnterCatch, LeaveTry, EndFinally,
    IteratorInit, IteratorMoveNext, IteratorClose,
    Jump, Branch, Break, Continue, Return,
    ...
}
```

### ExecutionPlanRunner

**File:** `Ast/TypedAstEvaluator.ExecutionPlanRunner.cs`

The interpreter for IR. Maintains:
- `_programCounter` - current instruction index
- `_instructions` - the instruction array
- `_flatSlots` - O(1) variable storage
- Environment stack

**Execution loop:**
```csharp
while (running)
{
    var instr = _instructions[_programCounter];
    var handler = _dispatchTable[(int)instr.Kind];
    var result = handler(this, instr, ref environment, context, out returnValue);

    switch (result) {
        case InstructionResult.Continue: _programCounter = instr.Next; break;
        case InstructionResult.Jump: /* _programCounter set by handler */ break;
        case InstructionResult.Yield: return returnValue;
        case InstructionResult.Return: return returnValue;
    }
}
```

---

## Function Execution Variants

### Sync Functions

**File:** `Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`

Uses IR execution by default. Falls back to AST walking for `with`/`eval`.

```
SyncFunctionInvoker.Invoke()
  -> Try IR: ExecutionPlanRunner
  -> Fallback: EvaluateBody() [AST walking]
```

### Generators (Deep Dive)

**Files:**
- `Ast/TypedAstEvaluator.SyncGeneratorInvoker.cs` - the callable
- `Ast/TypedAstEvaluator.ExecutionPlanRunner.cs` - the state machine
- `Execution/Emitters/YieldEmitter.cs` - IR generation

Generators use IR execution with explicit pause/resume points.

**Invocation creates iterator:**
```csharp
// SyncGeneratorInvoker.Invoke()
var runner = CreateRunner(arguments, thisValue);
runner.Initialize();  // Sets up environment, doesn't execute
return runner.CreateGeneratorObject();  // Returns iterator with .next()/.return()/.throw()
```

**Yield IR pattern:**

A `yield expr` statement becomes:
```
YieldInstruction(expression)  -> pause, return { value: expr, done: false }
StoreResumeValue(targetSlot)  -> on resume, store passed value
```

**Resume modes:**
```csharp
enum ResumeMode { Next, Return, Throw }

// .next(value) -> ResumeMode.Next, value stored in resume slot
// .return(value) -> ResumeMode.Return, triggers cleanup
// .throw(error) -> ResumeMode.Throw, error thrown from yield point
```

**HandleYield instruction handler:**
```csharp
private static InstructionResult HandleYield(...)
{
    // 1. Evaluate yield expression
    var yieldedValue = instruction.YieldExpression.EvaluateExpression(...);

    // 2. Create iterator result
    returnValue = CreateIteratorResult(yieldedValue, done: false);

    // 3. Save program counter (already at next instruction)
    runner._state = GeneratorState.Suspended;

    // 4. Return to caller - execution pauses here
    return InstructionResult.Yield;
}
```

**StoreResumeValue (on next .next() call):**
```csharp
private static InstructionResult HandleStoreResumeValue(...)
{
    var (resumeKind, resumePayload) = runner.ConsumeResumeValue();

    if (resumeKind == ResumePayloadKind.Throw)
        context.SetThrow(resumePayload);  // Will be caught or propagate
    else if (resumeKind == ResumePayloadKind.Return)
        context.SetReturn(resumePayload); // Early return
    else if (instruction.TargetSymbol is { } slot)
        StoreSymbolValue(environment, slot, resumePayload);  // Normal: x = yield

    return InstructionResult.Continue;
}
```

**yield* delegation:**

`yield* iterable` is more complex - it forwards .next()/.return()/.throw() to inner iterator:

```
YieldStarInstruction(iterable, stateSlot)
  -> Get iterator from iterable
  -> Loop: call inner.next(), yield each value
  -> On .return(): call inner.return() if exists
  -> On .throw(): call inner.throw() if exists
  -> When inner done: store final value, continue
```

Uses `YieldStarState` object to track delegation state across resumes.

### Async Functions (Deep Dive)

**Files:**
- `Ast/TypedAstEvaluator.AsyncFunctionInvoker.cs` - drives execution
- `Ast/TypedAstEvaluator.AsyncInvokerHelpers.cs` - shared utilities

Async functions reuse the generator IR but are driven internally (not exposed as iterator).

**Execution flow:**
```
AsyncFunctionInvoker.Execute()
  |
  +-> Create Promise with executor callback
  |
  +-> Inside executor:
      |
      +-> Create ExecutionPlanRunner (same as generator)
      +-> Initialize()
      +-> DriveToCompletion(ResumeMode.Next, undefined, resolve, reject)
```

**DriveToCompletion loop:**
```csharp
void DriveToCompletion(ResumeMode mode, JsValue argument, resolve, reject)
{
    var step = _inner.ExecuteAsyncStep(mode, argument);

    switch (step.Kind)
    {
        case Completed:
            resolve(step.Value);  // Done! Resolve promise
            break;

        case Yield:
            // Async functions don't yield externally
            // Treat as intermediate value, keep going
            DriveToCompletion(Next, step.Value, resolve, reject);
            break;

        case Throw:
            reject(step.Value);  // Uncaught error
            break;

        case Pending:
            // Hit an await on unresolved promise
            HandlePendingStep(step, resolve, reject);
            break;
    }
}
```

**Await handling:**

When `await promise` encounters an unresolved promise:

1. `EvaluationContext.IsPendingAwait` is set
2. `TryHandlePendingAwait()` detects this, saves state, returns `Pending`
3. `HandlePendingStep()` attaches .then() handlers to the promise:

```csharp
void HandlePendingStep(step, resolve, reject)
{
    var thenCallable = step.PendingPromise.GetProperty("then");

    // Create pooled callbacks to avoid allocation
    var (onFulfilled, onRejected) = AsyncResumeCallback.Rent(this, resolve, reject);

    // promise.then(onFulfilled, onRejected)
    thenCallable.Invoke([onFulfilled, onRejected], step.PendingPromise);
}
```

4. When promise settles, callback resumes:
```csharp
// AsyncResumeCallback.Invoke()
executor.DriveToCompletion(
    isRejection ? ResumeMode.Throw : ResumeMode.Next,
    args[0],  // resolved/rejected value
    resolve, reject);
```

**Callback pooling:**

`AsyncResumeCallback` uses object pools to avoid allocations:
- Callbacks created in pairs (fulfilled + rejected)
- Only one is invoked; it returns both to pool
- 32-entry pools for each type

### Async Generators

**File:** `Ast/TypedAstEvaluator.AsyncGeneratorInvoker.cs`

Combines both patterns:
- External iterator interface (`.next()` returns Promise)
- Internal await handling (suspends on unresolved promises)

```
asyncGen.next(value)
  -> Return Promise immediately
  -> Resume ExecutionPlanRunner
  -> On yield: resolve Promise with { value, done: false }
  -> On await pending: attach .then(), suspend
  -> On return: resolve Promise with { value, done: true }
  -> On throw: reject Promise
```

**Key difference from sync generators:**
Each `.next()` call returns a Promise, not the iterator result directly.

---

## JsEnvironment Variable Access

**File:** `JsEnvironment.cs`

Three levels of variable access, from slowest to fastest:

### 1. Named Access (Slowest)

Lookup by Symbol through scope chain:

```csharp
env.GetBindingValue(Symbol name)
  -> Walk scope chain
  -> For each scope: linear scan of _slots by name
  -> O(scopes * slots)
```

Used for: dynamic access, eval, with statements, unoptimized code.

### 2. Slot Access (Medium)

Direct index into scope's slot array:

```csharp
env.GetSlotRef(int slotIndex)
  -> return ref _slots[slotIndex].Value
  -> O(1) within known scope
```

Requires knowing which scope owns the variable. Used by `JsVariable`:

```csharp
struct JsVariable(JsEnvironment environment, int slotIndex)
{
    JsValue Read() => Environment.GetSlotRef(SlotIndex);
    void Write(JsValue v) => Environment.SetSlotDirect(SlotIndex, v);
}
```

### 3. Flat Slot Access (Fastest)

**IR-only optimization.** All variables across all scopes are assigned to a single flat array:

```csharp
// In ExecutionPlanRunner:
JsValue[]? _flatSlots;

// Access:
ref var value = ref _flatSlots[flatSlotId];
```

**How it works:**
1. At lowering time, `FlatSlotMappings` maps (scopeId, slotIndex) -> flatSlotId
2. `PushEnvironment` instruction copies current scope slots into flat array
3. Instructions like `IncrementSlotInstruction` have both `SlotIndex` and `FlatSlotId`
4. Fast path uses `FlatSlotId` when >= 0

```csharp
// Fast path in handler:
if (flatSlotId >= 0 && _flatSlots is not null)
{
    ref var value = ref _flatSlots[flatSlotId];
    // direct access, no scope lookup
}
```

**Benefits:**
- O(1) access regardless of scope depth
- No scope chain traversal
- Enables super-fast arithmetic paths (both operands in flat slots)

---

## Performance Patterns

### Fast/Slow Path Split

Hot instruction handlers are split into inlined fast paths and non-inlined slow paths:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static InstructionResult HandleIncrementSlot(...)
{
    // Fast path: ~30 lines, handles common case
    if (flatSlotId >= 0 && _flatSlots is not null)
    {
        // direct numeric increment
        return InstructionResult.Continue;
    }

    // Delegate to slow path
    return HandleIncrementSlotSlow(...);
}

[MethodImpl(MethodImplOptions.NoInlining)]
private static InstructionResult HandleIncrementSlotSlow(...)
{
    // Complex cases: scope lookup, type coercion, errors
}
```

**Why:** JIT inlines the tiny fast path into the hot loop. Slow path stays separate, doesn't bloat the loop.

### Dispatch Table

Handlers are stored in a delegate array indexed by `InstructionKind`:

```csharp
private static readonly InstructionHandler[] _dispatchTable = new InstructionHandler[43];

static ExecutionPlanRunner()
{
    _dispatchTable[(int)InstructionKind.Statement] = HandleStatement;
    _dispatchTable[(int)InstructionKind.IncrementSlot] = HandleIncrementSlot;
    // ...
}
```

**Why:** Faster than switch statement for many cases. Enables direct delegate invocation.

### Object Pooling

**Files:** `ObjectPool.cs`, `IRentable.cs`, `JsEnvironmentPool.cs`, `IteratorDriverStatePool.cs`

Frequently allocated objects are pooled to reduce GC pressure. This is critical for hot paths like loop iterations.

**Pooled types:**
- `JsEnvironment` - execution scopes (created per function call, loop iteration)
- `IteratorDriverState` - for-of loop state (iterator object, enumerator)
- `ForInDriverState` - for-in loop state (property keys)
- Various enumerators (`JsArrayPooledEnumerator`, `StringPooledEnumerator`, etc.)

**IRentable interface:**

All poolable objects implement this:

```csharp
internal interface IRentable
{
    void Activate(ILogger? logger = null);  // Called on rent
    void Reset(ILogger? logger = null);     // Called on return
}
```

**ObjectPool<T>:**

Lock-free fixed-size array pool using `Interlocked.CompareExchange`:

```csharp
internal sealed class ObjectPool<T>(int size, Func<T> factory) where T : class
{
    public T Rent(ILogger? logger = null)
    {
        // Try to find available item via CAS
        // If pool exhausted, create new via factory
    }

    public void Return(T item, ILogger? logger = null)
    {
        // Reset item, try to return via CAS
        // If pool full, item is abandoned to GC
    }
}
```

**Pooled<T> wrapper:**

RAII pattern ensures objects are returned:

```csharp
using var envHandle = JsEnvironmentPool.Rent(enclosing, isFunctionScope, isStrict);
var env = envHandle.Value;
// ... use env ...
// Automatically returned on dispose
```

**Why pooling matters:**

In a tight loop like `for (let i = 0; i < 1000000; i++)`:
- Each iteration creates a new block scope (`JsEnvironment`)
- Without pooling: 1M allocations, heavy GC pressure
- With pooling: ~32 allocations (pool size), objects reused

**Debug invariants:**

Pooling bugs (double-lease, use-after-return) are caught by debug assertions. See `how-to-debugging.md` for details on `PoolDebug` and `PoolGuard`.
