# Block Scope Closure Investigation

## Issue: #351 - Closures can't access block-scoped variables

### Problem Statement
Nested functions (closures) cannot access `let`/`const` variables defined in enclosing block scopes.

```javascript
function outer() {
    {
        let z = 42;
        function inner() {
            return z;  // ERROR: "z is not defined"
        }
        return inner();
    }
}
outer();
```

### Root Cause Analysis

The issue involves the interaction between:
1. **Execution Plan (IR)** - Functions are compiled to an instruction-based plan
2. **Slot Stamping** - Identifiers get `ScopeId` and `SlotIndex` for fast lookup
3. **Environment Chain** - Runtime scope chain with `ScopeId` for each environment
4. **Closure Capture** - Functions capture their enclosing environment at definition time

### Current State (as of investigation)

#### What's Working
1. **Block scope stamping in outer function's plan** - The block in `StatementInstruction` IS correctly stamped:
   - `ScopeId: 1000`
   - `SlotCount: 1`
   - `SlotMap: {z: 0}`

2. **Inner function's plan stamping** - The `ReturnInstruction` for `return z` IS correctly stamped:
   - `Return z - ScopeId: 1000, SlotIndex: 0`

3. **Cache sharing** - Both AST path and stamped path reference the SAME:
   - `FunctionExpression` object (same hash)
   - Cached `ExecutionPlan` object (same hash)

#### What's NOT Working
At runtime, "z is not defined" error still occurs despite correct stamping.

### Investigation Trace

#### Test Debug Output
```
outer plan has 2 instructions
  Instr: ReturnInstruction
  Instr: StatementInstruction
    Statement: BlockStatement
    Block ScopeId: 1000, SlotCount: 1
    Block SlotMap keys: z
    Stamped block has inner function: inner
    Stamped FunctionExpression hash: -520662654
    Stamped inner plan built: True
    Stamped inner plan hash: 618478411
    Stamped inner plan has 2 instructions
      Inner instr: ReturnInstruction
      Inner instr: ReturnInstruction
        Return z - ScopeId: 1000, SlotIndex: 0
AST block found: True
AST block ScopeId: -1, SlotCount: -1  (original AST not stamped)
inner function found: inner
AST FunctionExpression hash: -520662654  (SAME object!)
inner plan built: True
AST inner plan hash: 618478411           (SAME plan!)
return z (AST) - ScopeId: -1, SlotIndex: -1
```

### Execution Flow

1. **Outer function runs** via `ExecutionPlanRunner`
2. **StatementInstruction** wraps the stamped block (ScopeId=1000)
3. **Block is evaluated** via AST path: `statementInstruction.Statement.EvaluateStatementJsValue`
4. **EvaluateBlockSlowCore** should create environment with `scope.ScopeId = block.ScopeId`
5. **InstantiateLexicalBlockFunctions** creates `inner` with block's environment as closure
6. **inner() is called** - `SyncFunctionInvoker` uses the cached plan
7. **ReturnInstruction** evaluates identifier `z` with ScopeId=1000, SlotIndex=0
8. **FindByScopeId(1000)** should find the block's environment in the chain

### Key Code Paths

#### BlockStatementExtensions.cs - EvaluateBlockSlowCore
```csharp
scope.ScopeId = block.ScopeId;  // Should be 1000
scope.SetSlotMap(block.SlotMap);
if (block is { SlotCount: > 0, ScopeId: >= 0 })
{
    scope.InitializeSlots(block.SlotCount, block.ScopeId);
}
```

#### JsEnvironment.cs - TryReadIdentifierWithSlot
```csharp
if (scopeId >= 0 && slotIndex >= 0)
{
    var targetEnv = (ScopeId == scopeId) ? this : FindByScopeId(scopeId);
    // ... slot-based read
}
```

#### JsEnvironment.cs - FindByScopeId
```csharp
internal JsEnvironment? FindByScopeId(int scopeId)
{
    var env = this;
    while (env is not null)
    {
        if (env.ScopeId == scopeId) return env;
        env = env.Enclosing;
    }
    return null;
}
```

### Hypotheses to Test

1. **H1**: Block environment's `ScopeId` is not being set correctly at runtime
   - Need to verify `scope.ScopeId = block.ScopeId` is actually setting 1000

2. **H2**: Inner function's closure doesn't include the block's environment
   - Need to verify `_closure` in `SyncFunctionInvoker` is the block's environment

3. **H3**: `FindByScopeId(1000)` fails to find the environment in the chain
   - Need to trace the environment chain when inner() runs

4. **H4**: The stamped block in `StatementInstruction` is not used at runtime
   - Need to verify which block object is evaluated

### Files Modified

- `src/Asynkron.JsEngine/Ast/AstVisitor.cs` - Fixed BlockStatement visiting
- `src/Asynkron.JsEngine/Execution/IdentifierCollector.cs` - Added block scope ID tracking
- `src/Asynkron.JsEngine/Execution/SlotAssignmentRewriter.cs` - Added block scope rewriting
- `src/Asynkron.JsEngine/Execution/ExecutionPlanBuilder.cs` - Added nested function stamping

### Next Steps

1. Add runtime logging to trace:
   - What `ScopeId` the block environment gets
   - What `_closure` the inner function captures
   - What `FindByScopeId(1000)` returns

2. Verify the stamped block is actually being evaluated (not the original AST block)

3. Check if environment pooling is causing issues

### Related PR
- WIP PR #352 to preserve current work
