# ExecutionPlan & ExecutionPlanRunner Refactoring Plan

## Overview

Refactor the monolithic 2400+ line switch statement in `ExecutionPlanRunner` and the scattered emission logic in `ExecutionPlanBuilder` into a clean, extensible architecture using the Handler pattern and category-based emitters.

## Goals

1. **Maintainability**: Each instruction handler in its own focused file
2. **Extensibility**: Add new instructions without modifying a giant switch
3. **Testability**: Individual handlers can be unit tested in isolation
4. **Readability**: Category-based organization makes code discoverable
5. **Reduced duplication**: Shared completion handling extracted

---

## Phase 1: Infrastructure Setup

### 1.1 Create ExecutionState class
- [ ] Create `src/Asynkron.JsEngine/Execution/ExecutionState.cs`
- [ ] Move scattered state from ExecutionPlanRunner:
  - `_executionEnvironment` → `Environment`
  - `_programCounter` → `ProgramCounter`
  - `_tryStack` → `TryStack`
  - `_loopStack` → `LoopStack`
  - `_activeWithScopes` → `WithScopes`
  - `_state` → `GeneratorState`
  - `_resumeContext` → `ResumeContext`
  - `_pendingResumeKind`, `_pendingResumeValue`, `_pendingAwaitKey` → consolidated
- [ ] Add reference to `EvaluationContext` and `TypedAstEvaluator`
- [ ] Add helper methods for common operations

### 1.2 Create IInstructionHandler interface
- [ ] Create `src/Asynkron.JsEngine/Execution/Handlers/IInstructionHandler.cs`
```csharp
public interface IInstructionHandler
{
    /// <summary>
    /// Execute the instruction and return the next program counter.
    /// Return null to signal yield/suspend.
    /// </summary>
    int? Execute(ExecutionState state, ExecutionInstruction instruction);
}
```

### 1.3 Create InstructionDispatcher
- [ ] Create `src/Asynkron.JsEngine/Execution/InstructionDispatcher.cs`
- [ ] Implement handler registry (Dictionary<Type, IInstructionHandler>)
- [ ] Implement `Dispatch(ExecutionState, ExecutionInstruction)` method
- [ ] Consider using a static array indexed by instruction type ID for perf

### 1.4 Create CompletionHandler utility
- [ ] Create `src/Asynkron.JsEngine/Execution/Handlers/CompletionHandler.cs`
- [ ] Extract common completion handling logic:
  - `HandleThrowIfNeeded()` - find catch/finally, propagate
  - `HandleReturnIfNeeded()` - check try/finally, complete
  - `HandleYieldIfNeeded()` - record yield, suspend
  - `HandleBreakIfNeeded()` - resolve break target
  - `HandleContinueIfNeeded()` - resolve continue target
- [ ] Single entry point: `HandleCompletion(state, normalNext) -> int?`

---

## Phase 2: Extract Instruction Handlers

### 2.1 Control Flow Handlers
Create `src/Asynkron.JsEngine/Execution/Handlers/ControlFlow/`:

- [ ] `JumpHandler.cs` - Unconditional jump (simple, ~10 lines)
- [ ] `BranchHandler.cs` - Conditional branch (~30 lines)
- [ ] `BreakHandler.cs` - Break resolution (~40 lines)
- [ ] `ContinueHandler.cs` - Continue resolution (~40 lines)
- [ ] `ReturnHandler.cs` - Return with try/finally handling (~60 lines)

### 2.2 Scope Handlers
Create `src/Asynkron.JsEngine/Execution/Handlers/Scope/`:

- [ ] `PushEnvironmentHandler.cs` - Create child environment (~50 lines)
- [ ] `PopEnvironmentHandler.cs` - Return to parent (~30 lines)
- [ ] `EnterWithHandler.cs` - Enter with scope (~40 lines)
- [ ] `LeaveWithHandler.cs` - Leave with scope (~30 lines)

### 2.3 Loop Handlers
Create `src/Asynkron.JsEngine/Execution/Handlers/Loop/`:

- [ ] `LoopEnterHandler.cs` - Push loop context (~20 lines)
- [ ] `LoopExitHandler.cs` - Pop loop context (~20 lines)
- [ ] `IteratorInitHandler.cs` - Initialize iterator (~80 lines)
- [ ] `IteratorMoveNextHandler.cs` - Advance iterator (~450 lines, split further?)
- [ ] `IteratorCloseHandler.cs` - Close iterator (~30 lines)

**Note**: `IteratorMoveNextHandler` is very large. Consider splitting:
- [ ] `SyncIteratorMoveNextHandler.cs`
- [ ] `AsyncIteratorMoveNextHandler.cs`

### 2.4 Generator/Async Handlers
Create `src/Asynkron.JsEngine/Execution/Handlers/Generator/`:

- [ ] `YieldHandler.cs` - Simple yield (~50 lines)
- [ ] `YieldStarHandler.cs` - Delegation yield (~100 lines)
- [ ] `StoreResumeValueHandler.cs` - Store resume value (~30 lines)

### 2.5 Exception Handlers
Create `src/Asynkron.JsEngine/Execution/Handlers/Exception/`:

- [ ] `EnterTryHandler.cs` - Push try frame (~20 lines)
- [ ] `LeaveTryHandler.cs` - Mark normal exit (~20 lines)
- [ ] `EndFinallyHandler.cs` - Handle pending completions (~60 lines)
- [ ] `ThrowHandler.cs` - Evaluate and throw (~60 lines)

### 2.6 Statement Handlers
Create `src/Asynkron.JsEngine/Execution/Handlers/Statement/`:

- [ ] `StatementHandler.cs` - Generic AST statement evaluation (~80 lines)
- [ ] `EvaluateAndDiscardHandler.cs` - Expression statement (~40 lines)
- [ ] `VariableDeclarationHandler.cs` - var/let/const init (~100 lines)
- [ ] `BinaryOpHandler.cs` - Binary operations (~80 lines)
- [ ] `IncrementSlotHandler.cs` - ++/-- fast path (~70 lines)
- [ ] `FunctionDeclarationHandler.cs` - No-op for hoisted (~10 lines)
- [ ] `ClassDeclarationHandler.cs` - Class definition (~30 lines)

---

## Phase 3: Refactor ExecutionPlanRunner

### 3.1 Create new runner using handlers
- [ ] Create `ExecutionPlanRunner.Handlers.cs` (partial class)
- [ ] Initialize `InstructionDispatcher` in constructor
- [ ] Replace switch statement with dispatcher call
- [ ] Keep old switch as fallback initially (feature flag?)

### 3.2 Migrate incrementally
- [ ] Start with simple handlers (Jump, Branch, LoopEnter/Exit)
- [ ] Verify tests pass after each handler migration
- [ ] Profile to ensure no performance regression
- [ ] Move to more complex handlers (Yield, Iterator, Try)

### 3.3 Remove old switch statement
- [ ] Once all handlers migrated, remove switch
- [ ] Clean up any dead code
- [ ] Final test pass

---

## Phase 4: Extract IR Emitters

### 4.1 Create EmitContext
- [ ] Create `src/Asynkron.JsEngine/Execution/Emitters/EmitContext.cs`
- [ ] Encapsulate instruction list building
- [ ] Provide helpers: `Emit()`, `CurrentIndex`, `Patch()`, `EmitStatement()`
- [ ] Track label targets for patching

### 4.2 Loop Emitters
Create `src/Asynkron.JsEngine/Execution/Emitters/`:

- [ ] `LoopEmitter.cs` - Entry point for all loops
  - [ ] `EmitForLoop()` - Standard for loop
  - [ ] `EmitWhileLoop()` - While loop
  - [ ] `EmitDoWhileLoop()` - Do-while loop
  - [ ] `EmitForInLoop()` - For-in enumeration
  - [ ] `EmitForOfLoop()` - For-of iteration

### 4.3 Control Flow Emitters
- [ ] `ControlFlowEmitter.cs`
  - [ ] `EmitIf()` - If/else
  - [ ] `EmitSwitch()` - Switch statement (lowered to if chain)
  - [ ] `EmitBreak()` / `EmitContinue()` / `EmitReturn()`

### 4.4 Try/Catch Emitters
- [ ] `TryEmitter.cs`
  - [ ] `EmitTry()` - Try/catch/finally
  - [ ] `EmitThrow()` - Throw statement

### 4.5 Declaration Emitters
- [ ] `DeclarationEmitter.cs`
  - [ ] `EmitVariableDeclaration()` - var/let/const
  - [ ] `EmitFunctionDeclaration()` - Function hoisting
  - [ ] `EmitClassDeclaration()` - Class definition

### 4.6 Refactor ExecutionPlanBuilder
- [ ] Create `ExecutionPlanBuilder.Emitters.cs` (partial class)
- [ ] Replace inline emission with emitter calls
- [ ] Simplify `TryBuildStatement()` to dispatch by category:
```csharp
if (IsLoopStatement(stmt)) return LoopEmitter.TryEmit(stmt, ctx);
if (IsTryStatement(stmt)) return TryEmitter.TryEmit(stmt, ctx);
// etc.
```

---

## Phase 5: Testing & Validation

### 5.1 Unit tests for handlers
- [ ] Create `tests/Asynkron.JsEngine.Tests/Execution/Handlers/` directory
- [ ] Test each handler in isolation with mock ExecutionState
- [ ] Focus on edge cases (empty stacks, null values, etc.)

### 5.2 Integration tests
- [ ] Ensure all existing tests pass
- [ ] Run Test262 suite to verify no regressions
- [ ] Add tests for handler dispatch mechanism

### 5.3 Performance validation
- [ ] Run profiler before and after: `./tools/profile fib --cpu`
- [ ] Compare allocation patterns: `./tools/profile fib --memory`
- [ ] Benchmark key scenarios (loops, generators, async)
- [ ] Target: No more than 5% regression

---

## Phase 6: Documentation & Cleanup

### 6.1 Update documentation
- [ ] Update CLAUDE.md with new architecture
- [ ] Document handler pattern in docs/
- [ ] Add inline documentation to key interfaces

### 6.2 Final cleanup
- [ ] Remove any temporary fallback code
- [ ] Ensure consistent naming conventions
- [ ] Run code formatting
- [ ] Final review of all new files

---

## File Structure After Refactoring

```
src/Asynkron.JsEngine/Execution/
├── ExecutionPlan.cs                    # Unchanged
├── ExecutionInstruction.cs             # Unchanged
├── ExecutionPlanBuilder.cs             # Simplified, delegates to emitters
├── ExecutionPlanBuilder.Emitters.cs    # Partial - emitter integration
├── ExecutionState.cs                   # NEW - consolidated state
├── InstructionDispatcher.cs            # NEW - handler registry & dispatch
│
├── Handlers/
│   ├── IInstructionHandler.cs          # NEW - handler interface
│   ├── CompletionHandler.cs            # NEW - shared completion logic
│   │
│   ├── ControlFlow/
│   │   ├── JumpHandler.cs
│   │   ├── BranchHandler.cs
│   │   ├── BreakHandler.cs
│   │   ├── ContinueHandler.cs
│   │   └── ReturnHandler.cs
│   │
│   ├── Scope/
│   │   ├── PushEnvironmentHandler.cs
│   │   ├── PopEnvironmentHandler.cs
│   │   ├── EnterWithHandler.cs
│   │   └── LeaveWithHandler.cs
│   │
│   ├── Loop/
│   │   ├── LoopEnterHandler.cs
│   │   ├── LoopExitHandler.cs
│   │   ├── IteratorInitHandler.cs
│   │   ├── IteratorMoveNextHandler.cs
│   │   └── IteratorCloseHandler.cs
│   │
│   ├── Generator/
│   │   ├── YieldHandler.cs
│   │   ├── YieldStarHandler.cs
│   │   └── StoreResumeValueHandler.cs
│   │
│   ├── Exception/
│   │   ├── EnterTryHandler.cs
│   │   ├── LeaveTryHandler.cs
│   │   ├── EndFinallyHandler.cs
│   │   └── ThrowHandler.cs
│   │
│   └── Statement/
│       ├── StatementHandler.cs
│       ├── EvaluateAndDiscardHandler.cs
│       ├── VariableDeclarationHandler.cs
│       ├── BinaryOpHandler.cs
│       ├── IncrementSlotHandler.cs
│       ├── FunctionDeclarationHandler.cs
│       └── ClassDeclarationHandler.cs
│
└── Emitters/
    ├── EmitContext.cs                  # NEW - emission context
    ├── LoopEmitter.cs                  # NEW - for/while/do-while/for-in/for-of
    ├── ControlFlowEmitter.cs           # NEW - if/switch/break/continue/return
    ├── TryEmitter.cs                   # NEW - try/catch/finally/throw
    └── DeclarationEmitter.cs           # NEW - var/let/const/function/class
```

---

## Estimated Effort

| Phase | Description | Complexity |
|-------|-------------|------------|
| 1 | Infrastructure Setup | Medium |
| 2 | Extract Instruction Handlers | High (most work) |
| 3 | Refactor ExecutionPlanRunner | Medium |
| 4 | Extract IR Emitters | Medium |
| 5 | Testing & Validation | Medium |
| 6 | Documentation & Cleanup | Low |

---

## Risk Mitigation

1. **Performance regression**: Profile after each phase, revert if needed
2. **Breaking changes**: Keep old switch as fallback during migration
3. **Test failures**: Run full test suite after each handler extraction
4. **Complexity increase**: If any handler grows too large, split further

---

## Success Criteria

- [ ] All existing tests pass
- [ ] Test262 conformance unchanged
- [ ] No more than 5% performance regression
- [ ] Main switch statement eliminated
- [ ] Each handler file < 150 lines
- [ ] Code coverage maintained or improved
