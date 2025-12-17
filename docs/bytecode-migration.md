# Bytecode Migration Plan

This document outlines an incremental migration path from AST interpretation to full bytecode execution. Each phase is independently testable and delivers value.

## Current State

```
┌─────────────────────────────────────────────────────────┐
│                    Current Architecture                  │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  Sync functions:    AST → Direct interpretation          │
│                     (ExpressionExtensions, etc.)         │
│                                                          │
│  Generators:        AST → IR → Interpreter               │
│                     (SyncGeneratorIrBuilder)             │
│                                                          │
│  Async functions:   AST → IR → Interpreter + Scheduler   │
│                     (AsyncGeneratorInstance)             │
│                                                          │
│  IR has:            Control flow instructions            │
│                     Expressions embedded as AST nodes    │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

## Target State

```
┌─────────────────────────────────────────────────────────┐
│                    Target Architecture                   │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  All functions:     AST → Bytecode → VM                  │
│                                                          │
│  Bytecode has:      Expression instructions              │
│                     Control flow instructions            │
│                     Static variable slots                │
│                     Upvalue references for closures      │
│                                                          │
│  Single VM:         Stack-based execution                │
│                     Explicit call frames                 │
│                     No C# recursion for JS calls         │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

## Migration Phases

### Phase 0: Preparation (Foundation)

**Goal:** Set up infrastructure without changing behavior.

#### 0.1 Create Bytecode Types

```csharp
// src/Asynkron.JsEngine/Bytecode/Opcode.cs
public enum Opcode : byte
{
    // Stack
    Pop,
    Dup,

    // Literals
    PushUndefined,
    PushNull,
    PushTrue,
    PushFalse,
    PushInt8,      // 1-byte int follows
    PushInt32,     // 4-byte int follows
    PushDouble,    // 8-byte double follows
    PushString,    // constant pool index follows

    // Variables
    LoadLocal,     // slot index follows
    StoreLocal,
    LoadGlobal,    // name index follows
    StoreGlobal,
    LoadClosure,   // upvalue index follows
    StoreClosure,

    // Operators
    Add,
    Sub,
    Mul,
    Div,
    Mod,
    Neg,
    LessThan,
    LessEqual,
    GreaterThan,
    GreaterEqual,
    Equal,
    StrictEqual,
    NotEqual,
    StrictNotEqual,
    LogicalNot,
    Typeof,

    // Property access
    GetProperty,   // name index follows
    SetProperty,
    GetElement,    // computed
    SetElement,

    // Calls
    Call,          // arg count follows
    CallMethod,    // name index + arg count
    New,

    // Control flow
    Jump,
    JumpIfTrue,
    JumpIfFalse,
    JumpIfNullish,

    // Functions
    Return,

    // Generator/Async
    Yield,
    YieldStar,
    Await,

    // Exception
    EnterTry,
    LeaveTry,
    Throw,

    // Objects
    CreateObject,
    CreateArray,
    DefineProperty,
}
```

```csharp
// src/Asynkron.JsEngine/Bytecode/BytecodeFunction.cs
public sealed class BytecodeFunction
{
    public required byte[] Code { get; init; }
    public required object[] Constants { get; init; }  // strings, numbers, nested functions
    public required int LocalCount { get; init; }
    public required int ArgCount { get; init; }
    public required int StackSize { get; init; }       // max stack depth needed
    public required UpvalueDescriptor[] Upvalues { get; init; }
    public required ExceptionHandler[] Handlers { get; init; }
    public required SourceMap? SourceMap { get; init; }

    // Metadata
    public Symbol? Name { get; init; }
    public bool IsGenerator { get; init; }
    public bool IsAsync { get; init; }
    public bool IsStrict { get; init; }
}

public readonly record struct UpvalueDescriptor(
    bool IsLocal,      // true = capture from parent's locals, false = from parent's upvalues
    int Index);        // slot or upvalue index in parent

public readonly record struct ExceptionHandler(
    int TryStart,
    int TryEnd,
    int CatchStart,    // -1 if no catch
    int FinallyStart,  // -1 if no finally
    int StackDepth,
    int? CatchLocal);  // local slot for catch binding
```

#### 0.2 Create Skeleton VM

```csharp
// src/Asynkron.JsEngine/Bytecode/VirtualMachine.cs
public sealed class VirtualMachine
{
    private readonly JsValue[] _stack;
    private int _sp;

    private readonly CallFrame[] _frames;
    private int _fp;

    private CallFrame _currentFrame;

    public JsValue Execute(BytecodeFunction function, JsValue thisArg, JsValue[] args)
    {
        // TODO: Implement in Phase 2
        throw new NotImplementedException("Bytecode VM not yet implemented");
    }
}

internal struct CallFrame
{
    public BytecodeFunction Function;
    public byte[] Code;
    public int PC;
    public int StackBase;
    public JsValue[] Locals;
    public Upvalue[] Upvalues;
    public JsValue ThisBinding;
}
```

**Deliverable:** New types compile, no behavior change, all tests pass.

---

### Phase 1: Expression Compiler

**Goal:** Compile expressions to bytecode, execute via new VM, keep control flow in IR.

#### 1.1 Expression Bytecode Emitter

```csharp
// src/Asynkron.JsEngine/Bytecode/ExpressionCompiler.cs
public sealed class ExpressionCompiler
{
    private readonly List<byte> _code = new();
    private readonly List<object> _constants = new();
    private readonly Dictionary<Symbol, int> _localSlots;

    public CompiledExpression Compile(ExpressionNode expression)
    {
        EmitExpression(expression);
        return new CompiledExpression(_code.ToArray(), _constants.ToArray());
    }

    private void EmitExpression(ExpressionNode expr)
    {
        switch (expr)
        {
            case LiteralExpression lit:
                EmitLiteral(lit);
                break;

            case IdentifierExpression id:
                EmitIdentifier(id);
                break;

            case BinaryExpression bin:
                EmitExpression(bin.Left);
                EmitExpression(bin.Right);
                EmitBinaryOp(bin.Operator);
                break;

            case CallExpression call:
                EmitExpression(call.Callee);
                foreach (var arg in call.Arguments)
                    EmitExpression(arg);
                Emit(Opcode.Call, (byte)call.Arguments.Length);
                break;

            // ... etc
        }
    }

    private void EmitLiteral(LiteralExpression lit)
    {
        switch (lit.Value)
        {
            case null:
                Emit(Opcode.PushNull);
                break;
            case bool b:
                Emit(b ? Opcode.PushTrue : Opcode.PushFalse);
                break;
            case int i when i >= -128 && i <= 127:
                Emit(Opcode.PushInt8, (byte)i);
                break;
            case int i:
                Emit(Opcode.PushInt32);
                EmitInt32(i);
                break;
            case double d:
                Emit(Opcode.PushDouble);
                EmitDouble(d);
                break;
            case string s:
                var index = AddConstant(s);
                Emit(Opcode.PushString);
                EmitInt16((short)index);
                break;
        }
    }
}
```

#### 1.2 Expression VM (Minimal)

```csharp
public JsValue EvaluateExpression(CompiledExpression expr, JsEnvironment env)
{
    var code = expr.Code;
    var constants = expr.Constants;
    var stack = new JsValue[16];  // Small stack for single expression
    var sp = 0;
    var pc = 0;

    while (pc < code.Length)
    {
        var op = (Opcode)code[pc++];
        switch (op)
        {
            case Opcode.PushNull:
                stack[sp++] = JsValue.Null;
                break;

            case Opcode.PushTrue:
                stack[sp++] = JsValue.True;
                break;

            case Opcode.PushInt8:
                stack[sp++] = (sbyte)code[pc++];
                break;

            case Opcode.Add:
                var r = stack[--sp];
                var l = stack[--sp];
                stack[sp++] = JsOps.Add(l, r);
                break;

            case Opcode.LoadLocal:
                var slot = code[pc++];
                stack[sp++] = locals[slot];
                break;

            // ... minimal set
        }
    }

    return stack[0];
}
```

#### 1.3 Hybrid IR: Bytecode Expressions

Modify IR to optionally use compiled expressions:

```csharp
// Option A: New instruction type
record BytecodeExpressionInstruction(CompiledExpression Expr, int NextIndex);

// Option B: Modify existing instructions
record BranchInstruction(
    ExpressionNode? Condition,           // Old way (nullable now)
    CompiledExpression? CompiledCondition, // New way
    int ConsequentIndex,
    int AlternateIndex);
```

#### 1.4 Feature Flag

```csharp
public class JsEngineOptions
{
    /// <summary>
    /// When true, expressions are compiled to bytecode.
    /// When false, expressions use AST interpretation (legacy).
    /// </summary>
    public bool UseBytecodeExpressions { get; set; } = false;
}
```

**Deliverable:**
- Expressions can be compiled to bytecode
- Feature flag allows A/B testing
- Run test suite with flag on and off
- Measure performance difference

**Expected Performance Gain:** ~2-5x for expression-heavy code.

---

### Phase 2: Scope Analysis

**Goal:** Resolve variables at compile time, eliminate runtime environment chain lookup.

#### 2.1 Scope Tree Builder

```csharp
// src/Asynkron.JsEngine/Bytecode/ScopeAnalyzer.cs
public sealed class ScopeAnalyzer
{
    public ScopeInfo Analyze(FunctionExpression function)
    {
        var scope = new ScopeBuilder(parent: null, isStrict: function.IsStrict);

        // Add parameters
        foreach (var param in function.Parameters)
            scope.DeclareParameter(param);

        // Analyze body
        AnalyzeStatements(function.Body.Statements, scope);

        return scope.Build();
    }

    private void AnalyzeStatements(IEnumerable<StatementNode> statements, ScopeBuilder scope)
    {
        // First pass: collect declarations (hoisting)
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case VariableDeclaration { Kind: VariableKind.Var } decl:
                    foreach (var d in decl.Declarators)
                        scope.DeclareVar(d.Target);
                    break;

                case FunctionDeclaration func:
                    scope.DeclareFunction(func.Name);
                    break;
            }
        }

        // Second pass: analyze usage and nested scopes
        foreach (var stmt in statements)
            AnalyzeStatement(stmt, scope);
    }

    private void AnalyzeExpression(ExpressionNode expr, ScopeBuilder scope)
    {
        switch (expr)
        {
            case IdentifierExpression id:
                scope.ReferenceVariable(id.Name);  // Mark as used
                break;

            case FunctionExpression func:
                // New scope for function body
                var funcScope = new ScopeBuilder(parent: scope, isStrict: func.IsStrict);
                // ... analyze function
                break;

            // ... recurse into children
        }
    }
}
```

#### 2.2 Scope Info Output

```csharp
public sealed class ScopeInfo
{
    public required ImmutableArray<VariableInfo> Variables { get; init; }
    public required ImmutableArray<ScopeInfo> Children { get; init; }
    public required bool NeedsDynamicScope { get; init; }  // has eval/with
    public required int LocalSlotCount { get; init; }
    public required int UpvalueCount { get; init; }
}

public sealed class VariableInfo
{
    public required Symbol Name { get; init; }
    public required VariableKind Kind { get; init; }
    public required int SlotIndex { get; init; }
    public required bool IsCaptured { get; init; }      // Used by nested function
    public required bool IsParameter { get; init; }
}
```

#### 2.3 Integrate with Expression Compiler

```csharp
public sealed class ExpressionCompiler
{
    private readonly ScopeInfo _scope;

    private void EmitIdentifier(IdentifierExpression id)
    {
        var resolution = _scope.Resolve(id.Name);

        switch (resolution)
        {
            case LocalVariable local:
                Emit(Opcode.LoadLocal, (byte)local.SlotIndex);
                break;

            case UpvalueVariable upvalue:
                Emit(Opcode.LoadClosure, (byte)upvalue.Index);
                break;

            case GlobalVariable:
                var nameIndex = AddConstant(id.Name);
                Emit(Opcode.LoadGlobal);
                EmitInt16((short)nameIndex);
                break;

            case DynamicVariable:
                // Fallback for eval/with contexts
                var nameIdx = AddConstant(id.Name);
                Emit(Opcode.LoadDynamic);
                EmitInt16((short)nameIdx);
                break;
        }
    }
}
```

**Deliverable:**
- Variables resolved at compile time
- Local access is O(1) array index, not O(n) chain walk
- Captured variables identified for closure handling

**Expected Performance Gain:** ~2-3x for variable-heavy code.

---

### Phase 3: Full Function Bytecode

**Goal:** Compile entire functions to bytecode, not just expressions.

#### 3.1 Function Compiler

```csharp
// src/Asynkron.JsEngine/Bytecode/FunctionCompiler.cs
public sealed class FunctionCompiler
{
    private readonly List<byte> _code = new();
    private readonly List<object> _constants = new();
    private readonly ScopeInfo _scope;
    private readonly Stack<LoopContext> _loops = new();
    private int _stackDepth;
    private int _maxStackDepth;

    public BytecodeFunction Compile(FunctionExpression function)
    {
        var scope = _scopeAnalyzer.Analyze(function);

        // Compile body
        CompileStatements(function.Body.Statements);

        // Implicit return undefined
        Emit(Opcode.PushUndefined);
        Emit(Opcode.Return);

        return new BytecodeFunction
        {
            Code = _code.ToArray(),
            Constants = _constants.ToArray(),
            LocalCount = scope.LocalSlotCount,
            ArgCount = function.Parameters.Length,
            StackSize = _maxStackDepth,
            Upvalues = BuildUpvalueDescriptors(scope),
            Handlers = _exceptionHandlers.ToArray(),
            IsGenerator = function.IsGenerator,
            IsAsync = function.IsAsync,
            IsStrict = function.IsStrict,
        };
    }

    private void CompileStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case ExpressionStatement expr:
                CompileExpression(expr.Expression);
                Emit(Opcode.Pop);  // Discard result
                break;

            case ReturnStatement ret:
                if (ret.Expression != null)
                    CompileExpression(ret.Expression);
                else
                    Emit(Opcode.PushUndefined);
                Emit(Opcode.Return);
                break;

            case IfStatement ifStmt:
                CompileIf(ifStmt);
                break;

            case WhileStatement whileStmt:
                CompileWhile(whileStmt);
                break;

            case ForStatement forStmt:
                CompileFor(forStmt);
                break;

            // ... all statement types
        }
    }

    private void CompileIf(IfStatement stmt)
    {
        CompileExpression(stmt.Condition);

        var jumpToElse = EmitJump(Opcode.JumpIfFalse);

        CompileStatement(stmt.Then);

        if (stmt.Else != null)
        {
            var jumpToEnd = EmitJump(Opcode.Jump);
            PatchJump(jumpToElse);
            CompileStatement(stmt.Else);
            PatchJump(jumpToEnd);
        }
        else
        {
            PatchJump(jumpToElse);
        }
    }

    private void CompileWhile(WhileStatement stmt)
    {
        var loopStart = CurrentOffset;

        _loops.Push(new LoopContext(loopStart, breakPatches: new()));

        CompileExpression(stmt.Condition);
        var exitJump = EmitJump(Opcode.JumpIfFalse);

        CompileStatement(stmt.Body);

        EmitLoop(loopStart);  // Jump back
        PatchJump(exitJump);

        // Patch all break statements
        var loop = _loops.Pop();
        foreach (var breakPatch in loop.BreakPatches)
            PatchJump(breakPatch);
    }
}
```

#### 3.2 Full VM Implementation

```csharp
public sealed class VirtualMachine
{
    private JsValue[] _stack;
    private int _sp;
    private CallFrame[] _frames;
    private int _fp;

    public JsValue Execute(BytecodeFunction function, JsValue thisArg, JsValue[] args)
    {
        // Initialize first frame
        _frames[0] = new CallFrame
        {
            Function = function,
            Code = function.Code,
            PC = 0,
            StackBase = 0,
            Locals = InitLocals(function, args),
            ThisBinding = thisArg,
        };
        _fp = 0;

        return Run();
    }

    private JsValue Run()
    {
        ref var frame = ref _frames[_fp];
        var code = frame.Code;
        var constants = frame.Function.Constants;
        var locals = frame.Locals;

        while (true)
        {
            var op = (Opcode)code[frame.PC++];

            switch (op)
            {
                case Opcode.PushUndefined:
                    _stack[_sp++] = JsValue.Undefined;
                    break;

                case Opcode.PushInt8:
                    _stack[_sp++] = (sbyte)code[frame.PC++];
                    break;

                case Opcode.LoadLocal:
                    _stack[_sp++] = locals[code[frame.PC++]];
                    break;

                case Opcode.StoreLocal:
                    locals[code[frame.PC++]] = _stack[--_sp];
                    break;

                case Opcode.Add:
                    {
                        var r = _stack[--_sp];
                        var l = _stack[--_sp];
                        _stack[_sp++] = JsOps.Add(l, r);
                    }
                    break;

                case Opcode.Jump:
                    frame.PC = ReadInt16(code, ref frame.PC);
                    break;

                case Opcode.JumpIfFalse:
                    {
                        var target = ReadInt16(code, ref frame.PC);
                        if (!_stack[--_sp].ToBoolean())
                            frame.PC = target;
                    }
                    break;

                case Opcode.Call:
                    {
                        var argCount = code[frame.PC++];
                        var callee = _stack[_sp - argCount - 1];

                        if (callee is BytecodeJsFunction bcFunc)
                        {
                            // JS-to-JS call: push frame, no C# recursion
                            PushFrame(bcFunc.Bytecode, argCount);
                            frame = ref _frames[_fp];
                            code = frame.Code;
                            constants = frame.Function.Constants;
                            locals = frame.Locals;
                        }
                        else if (callee is NativeFunction native)
                        {
                            // Native call: invoke C# method
                            var args = PopArgs(argCount);
                            var result = native.Invoke(args);
                            _stack[_sp++] = result;
                        }
                    }
                    break;

                case Opcode.Return:
                    {
                        var result = _stack[--_sp];

                        if (_fp == 0)
                            return result;  // Done!

                        // Pop frame, continue caller
                        _sp = frame.StackBase;
                        _fp--;
                        frame = ref _frames[_fp];
                        code = frame.Code;
                        constants = frame.Function.Constants;
                        locals = frame.Locals;
                        _stack[_sp++] = result;
                    }
                    break;

                // ... all opcodes
            }
        }
    }
}
```

#### 3.3 Dual-Path Execution

During migration, support both paths:

```csharp
public class JsFunction
{
    // Old: AST-based
    public FunctionExpression? AstBody { get; }

    // New: Bytecode
    public BytecodeFunction? Bytecode { get; }

    public JsValue Invoke(JsValue thisArg, JsValue[] args)
    {
        if (Bytecode != null)
            return _vm.Execute(Bytecode, thisArg, args);
        else
            return _astEvaluator.EvaluateFunction(AstBody, thisArg, args);
    }
}
```

**Deliverable:**
- Regular functions compile to bytecode
- JS-to-JS calls don't use C# recursion
- Feature flag to switch between AST and bytecode

**Expected Performance Gain:** ~5-10x for call-heavy code, eliminates stack overflow risk.

---

### Phase 4: Generator/Async Unification

**Goal:** Generators and async functions use same bytecode, replacing current IR.

#### 4.1 Generator Bytecode

Add suspend/resume capability to VM:

```csharp
public sealed class GeneratorInstance
{
    private readonly VirtualMachine _vm;
    private readonly BytecodeFunction _function;
    private GeneratorState _state;

    // Saved execution state
    private int _savedPC;
    private int _savedSP;
    private JsValue[] _savedStack;
    private JsValue[] _locals;
    private Upvalue[] _upvalues;

    public JsIteratorResult Next(JsValue? sentValue)
    {
        if (_state == GeneratorState.Completed)
            return JsIteratorResult.Done(JsValue.Undefined);

        _state = GeneratorState.Executing;

        // Resume execution
        var result = _vm.ResumeGenerator(this, sentValue);

        if (result.IsYield)
        {
            _state = GeneratorState.SuspendedYield;
            SaveState();
            return JsIteratorResult.Value(result.Value);
        }
        else
        {
            _state = GeneratorState.Completed;
            return JsIteratorResult.Done(result.Value);
        }
    }
}
```

#### 4.2 Yield/Await Opcodes

```csharp
case Opcode.Yield:
    {
        var value = _stack[--_sp];
        return new SuspendResult(SuspendKind.Yield, value);
    }

case Opcode.Await:
    {
        var promise = _stack[--_sp];
        if (promise is JsPromise { IsSettled: true } settled)
        {
            // Already resolved, continue immediately
            _stack[_sp++] = settled.Value;
        }
        else
        {
            // Must suspend
            return new SuspendResult(SuspendKind.Await, promise);
        }
    }
    break;
```

#### 4.3 Remove Old IR

Once generators work on bytecode:

```csharp
// Delete these files:
// - src/Asynkron.JsEngine/Execution/GeneratorIr.cs
// - src/Asynkron.JsEngine/Execution/SyncGeneratorIrBuilder.cs
// - src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedGeneratorInstance.cs
```

**Deliverable:**
- Generators and async use bytecode
- Single execution engine for all code
- Old IR code removed

---

### Phase 5: Closure Optimization

**Goal:** Efficient closure handling with upvalues.

#### 5.1 Upvalue Implementation

```csharp
public abstract class Upvalue
{
    public abstract JsValue Value { get; set; }
}

// Variable still on stack (function hasn't returned)
public sealed class OpenUpvalue : Upvalue
{
    private readonly JsValue[] _locals;
    private readonly int _slot;

    public override JsValue Value
    {
        get => _locals[_slot];
        set => _locals[_slot] = value;
    }
}

// Variable moved to heap (function returned)
public sealed class ClosedUpvalue : Upvalue
{
    private JsValue _value;

    public override JsValue Value
    {
        get => _value;
        set => _value = value;
    }
}
```

#### 5.2 Close Upvalues on Return

```csharp
case Opcode.Return:
    {
        var result = _stack[--_sp];

        // Close any open upvalues pointing to this frame's locals
        CloseUpvalues(frame.StackBase);

        // ... rest of return handling
    }
    break;

private void CloseUpvalues(int stackBase)
{
    // Find all open upvalues pointing to locals >= stackBase
    // Convert them to closed upvalues (copy value to heap)
    foreach (var upvalue in _openUpvalues)
    {
        if (upvalue.StackIndex >= stackBase)
        {
            upvalue.Close();  // Converts to ClosedUpvalue
        }
    }
}
```

#### 5.3 Flat Closure Optimization

When possible, copy values instead of using indirection:

```csharp
// If variable is never mutated after capture, just copy the value
public sealed class FlatClosure
{
    public JsValue[] CopiedValues { get; }  // Direct copy, no indirection
}
```

**Deliverable:**
- Closures work correctly with bytecode
- Optimized for common case (non-mutated captures)

---

### Phase 6: Remove AST Interpretation

**Goal:** Delete all AST interpretation code.

#### 6.1 Remove Old Evaluators

```csharp
// Delete:
// - src/Asynkron.JsEngine/Ast/BinaryExpressionExtensions.cs
// - src/Asynkron.JsEngine/Ast/CallExpressionExtensions.cs
// - src/Asynkron.JsEngine/Ast/MemberExpressionExtensions.cs
// - src/Asynkron.JsEngine/Ast/*Extensions.cs (most of them)
// - src/Asynkron.JsEngine/Ast/TypedAstEvaluator.cs (if fully replaced)
```

#### 6.2 Simplify JsEnvironment

With static slots, the environment chain is mostly gone:

```csharp
// Old: linked list of environments
public class JsEnvironment
{
    public JsEnvironment? Parent { get; }
    public Dictionary<Symbol, JsValue> Bindings { get; }
}

// New: just for global and dynamic eval
public class JsGlobalEnvironment
{
    public JsObject GlobalObject { get; }
}
```

**Deliverable:**
- Codebase significantly smaller
- Single execution path
- Easier to maintain

---

### Phase 7: Optimizations (Future)

#### 7.1 Inline Caching

```csharp
// Property access caches shape + offset
case Opcode.GetProperty:
    {
        var nameIndex = ReadInt16(code, ref frame.PC);
        var cacheSlot = ReadInt16(code, ref frame.PC);

        var obj = _stack[--_sp];
        var cache = frame.Function.InlineCaches[cacheSlot];

        if (obj.Shape == cache.CachedShape)
        {
            // Fast path: direct slot access
            _stack[_sp++] = obj.Slots[cache.CachedOffset];
        }
        else
        {
            // Slow path: full lookup + cache update
            var (shape, offset, value) = obj.LookupProperty(name);
            cache.Update(shape, offset);
            _stack[_sp++] = value;
        }
    }
    break;
```

#### 7.2 Bytecode Optimization Passes

```csharp
public static class BytecodeOptimizer
{
    public static BytecodeFunction Optimize(BytecodeFunction input)
    {
        var code = input.Code.ToList();

        // Peephole optimizations
        ConstantFolding(code);
        DeadCodeElimination(code);
        JumpThreading(code);

        return input with { Code = code.ToArray() };
    }
}
```

#### 7.3 Superinstructions

Combine common sequences:

```csharp
// Instead of: LoadLocal(0), LoadLocal(1), Add
// Single instruction: AddLocals(0, 1)

case Opcode.AddLocals:
    {
        var left = code[frame.PC++];
        var right = code[frame.PC++];
        _stack[_sp++] = JsOps.Add(locals[left], locals[right]);
    }
    break;
```

---

## Testing Strategy

### Each Phase

1. **Unit tests**: Test compiler output for specific constructs
2. **Integration tests**: Run existing test suite with new path
3. **Comparison tests**: Run both paths, compare results
4. **Performance tests**: Benchmark both paths

### Feature Flags

```csharp
public class JsEngineOptions
{
    public bool UseBytecodeExpressions { get; set; }  // Phase 1
    public bool UseBytecodeStatements { get; set; }   // Phase 3
    public bool UseBytecodeGenerators { get; set; }   // Phase 4
    public bool UseBytecodeOnly { get; set; }         // Phase 6
}
```

### Test262 Compatibility

Run Test262 suite after each phase to catch regressions.

---

## Timeline Estimate

| Phase | Scope | Complexity |
|-------|-------|------------|
| Phase 0 | Infrastructure | Low |
| Phase 1 | Expression bytecode | Medium |
| Phase 2 | Scope analysis | Medium |
| Phase 3 | Full function bytecode | High |
| Phase 4 | Generator unification | Medium |
| Phase 5 | Closure optimization | Medium |
| Phase 6 | Remove old code | Low |
| Phase 7 | Optimizations | Ongoing |

Each phase is independently valuable and can be shipped separately.

---

## Rollback Plan

Each phase has a feature flag. If issues arise:

1. Disable flag in production
2. Fix issues
3. Re-enable with fix

Old code path remains until Phase 6, providing safety net.

## Files to Create

```
src/Asynkron.JsEngine/
├── Bytecode/
│   ├── Opcode.cs              # Phase 0
│   ├── BytecodeFunction.cs    # Phase 0
│   ├── VirtualMachine.cs      # Phase 0 skeleton, Phase 3 full
│   ├── ExpressionCompiler.cs  # Phase 1
│   ├── ScopeAnalyzer.cs       # Phase 2
│   ├── FunctionCompiler.cs    # Phase 3
│   ├── Upvalue.cs             # Phase 5
│   └── BytecodeOptimizer.cs   # Phase 7
```

## Files to Delete (Phase 6)

```
src/Asynkron.JsEngine/
├── Ast/
│   ├── *Extensions.cs         # Most of these
│   └── TypedAstEvaluator*.cs  # If fully replaced
├── Execution/
│   ├── GeneratorIr.cs
│   ├── SyncGeneratorIrBuilder.cs
│   └── LoopNormalizer.cs      # If loops compiled directly
```
