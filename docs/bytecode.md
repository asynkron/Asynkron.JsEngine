# Full Bytecode Architecture

This document explores the possibility of extending JsEngine2's current IR approach to compile **all** JavaScript to bytecode, not just generators and async functions.

## Current vs Full Bytecode

### Current Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Current Architecture                  │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  Sync code:       AST → direct interpretation            │
│                   (expressions evaluated via AST walk)   │
│                                                          │
│  Generators:      AST → IR (control flow only)           │
│                   (expressions still AST nodes in IR)    │
│                                                          │
│  Example IR instruction:                                 │
│    BranchInstruction(                                    │
│      Condition: BinaryExpression(x, <, 3),  ← AST node!  │
│      ConsequentIndex: 5,                                 │
│      AlternateIndex: 10)                                 │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

### Full Bytecode Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Full Bytecode Architecture            │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  All code:        AST → Bytecode → Execution             │
│                   (everything is instructions)           │
│                                                          │
│  Stack machine or register machine for values            │
│                                                          │
│  Example bytecode:                                       │
│    LoadLocal(0)        // x                              │
│    PushInt(3)                                            │
│    LessThan()                                            │
│    BranchIfFalse(10)   ← no embedded AST                 │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

This is exactly what production JavaScript engines do:
- **V8**: Ignition bytecode interpreter
- **SpiderMonkey**: Baseline bytecode interpreter
- **JavaScriptCore**: LLInt (Low Level Interpreter)

## New Instruction Types

The current IR handles control flow. Full bytecode needs expression evaluation too.

### Literal Instructions

```csharp
record PushUndefinedInstruction();
record PushNullInstruction();
record PushBoolInstruction(bool Value);
record PushIntInstruction(int Value);
record PushDoubleInstruction(double Value);
record PushStringInstruction(string Value);
record PushBigIntInstruction(BigInteger Value);
record PushRegExpInstruction(string Pattern, string Flags);
```

### Variable Instructions

```csharp
// Local variables (resolved to slots at compile time)
record LoadLocalInstruction(int SlotIndex);
record StoreLocalInstruction(int SlotIndex);

// Global variables
record LoadGlobalInstruction(Symbol Name);
record StoreGlobalInstruction(Symbol Name);

// Closure variables (upvalues)
record LoadClosureInstruction(int UpvalueIndex);
record StoreClosureInstruction(int UpvalueIndex);

// Dynamic lookup (for eval/with contexts)
record LoadDynamicInstruction(Symbol Name);
record StoreDynamicInstruction(Symbol Name);
```

### Operator Instructions

```csharp
// Arithmetic
record AddInstruction();
record SubtractInstruction();
record MultiplyInstruction();
record DivideInstruction();
record ModuloInstruction();
record ExponentiateInstruction();
record NegateInstruction();

// Bitwise
record BitwiseAndInstruction();
record BitwiseOrInstruction();
record BitwiseXorInstruction();
record BitwiseNotInstruction();
record LeftShiftInstruction();
record RightShiftInstruction();
record UnsignedRightShiftInstruction();

// Comparison
record LessThanInstruction();
record LessThanOrEqualInstruction();
record GreaterThanInstruction();
record GreaterThanOrEqualInstruction();
record EqualInstruction();
record NotEqualInstruction();
record StrictEqualInstruction();
record StrictNotEqualInstruction();

// Logical
record LogicalNotInstruction();
// Note: && and || need short-circuit branches, not instructions

// Typeof/instanceof
record TypeofInstruction();
record InstanceofInstruction();
record InInstruction();
```

### Property Access Instructions

```csharp
// Static property (known at compile time)
record GetPropertyInstruction(Symbol Name);
record SetPropertyInstruction(Symbol Name);
record DeletePropertyInstruction(Symbol Name);

// Computed property (key on stack)
record GetComputedPropertyInstruction();
record SetComputedPropertyInstruction();
record DeleteComputedPropertyInstruction();

// Optional chaining
record GetPropertyOptionalInstruction(Symbol Name, int NullishTarget);
record GetComputedPropertyOptionalInstruction(int NullishTarget);
```

### Call Instructions

```csharp
// Function call: func(args...)
record CallInstruction(int ArgCount);

// Method call: obj.method(args...) - keeps 'this' binding
record CallMethodInstruction(Symbol Name, int ArgCount);
record CallComputedMethodInstruction(int ArgCount);

// Constructor: new Func(args...)
record NewInstruction(int ArgCount);

// Spread handling
record CallWithSpreadInstruction(int ArgCount, bool[] SpreadPositions);

// Super
record CallSuperInstruction(int ArgCount);
record GetSuperPropertyInstruction(Symbol Name);
```

### Object/Array Instructions

```csharp
// Object creation
record CreateObjectInstruction();
record DefineOwnPropertyInstruction(Symbol Name);
record DefineComputedPropertyInstruction();
record DefineGetterInstruction(Symbol Name);
record DefineSetterInstruction(Symbol Name);

// Array creation
record CreateArrayInstruction(int Length);
record ArrayPushInstruction();
record ArraySpreadInstruction();

// Destructuring helpers
record DestructureArrayInstruction(int Count, bool HasRest);
record DestructureObjectInstruction(Symbol[] Keys, bool HasRest);
```

### Function Instructions

```csharp
// Function/class creation
record CreateFunctionInstruction(FunctionBytecode Template, int[] UpvalueSlots);
record CreateGeneratorFunctionInstruction(FunctionBytecode Template, int[] UpvalueSlots);
record CreateAsyncFunctionInstruction(FunctionBytecode Template, int[] UpvalueSlots);
record CreateAsyncGeneratorFunctionInstruction(FunctionBytecode Template, int[] UpvalueSlots);
record CreateArrowFunctionInstruction(FunctionBytecode Template, int[] UpvalueSlots);

// Class
record CreateClassInstruction(ClassBytecode Template);
record DefineMethodInstruction(Symbol Name, MethodKind Kind);
record DefineStaticMethodInstruction(Symbol Name, MethodKind Kind);
record DefineFieldInstruction(Symbol Name);
record DefineStaticFieldInstruction(Symbol Name);
```

### Control Flow Instructions

```csharp
// Already have most of these in current IR
record JumpInstruction(int Target);
record BranchIfTrueInstruction(int Target);
record BranchIfFalseInstruction(int Target);
record BranchIfNullishInstruction(int Target);
record BranchIfNotNullishInstruction(int Target);

// Loops (could keep as pseudo-instructions or lower to jumps)
record LoopStartInstruction(int EndTarget);  // For debugging/profiling

// Exception handling
record EnterTryInstruction(int CatchTarget, int FinallyTarget);
record LeaveTryInstruction();
record ThrowInstruction();
record ReThrowInstruction();  // In catch block

// Generator/async
record YieldInstruction();
record YieldStarInstruction();
record AwaitInstruction();
record ReturnInstruction();
```

### Stack Manipulation

```csharp
record PopInstruction();
record DupInstruction();
record SwapInstruction();
record RotateInstruction(int Count);  // For complex expressions
```

## Execution Models

### Stack Machine

Simpler to implement, what Java/Python/.NET use:

```csharp
internal sealed class StackMachineInterpreter
{
    private readonly JsValue[] _stack = new JsValue[256];
    private int _sp;  // Stack pointer
    private int _pc;  // Program counter
    private readonly GeneratorInstruction[] _code;

    public JsValue Execute()
    {
        while (true)
        {
            var instruction = _code[_pc++];
            switch (instruction)
            {
                case PushIntInstruction push:
                    _stack[_sp++] = push.Value;
                    break;

                case LoadLocalInstruction load:
                    _stack[_sp++] = _locals[load.SlotIndex];
                    break;

                case AddInstruction:
                    var right = _stack[--_sp];
                    var left = _stack[--_sp];
                    _stack[_sp++] = JsValue.Add(left, right);
                    break;

                case BranchIfFalseInstruction branch:
                    if (!_stack[--_sp].ToBoolean())
                        _pc = branch.Target;
                    break;

                case ReturnInstruction:
                    return _sp > 0 ? _stack[--_sp] : JsValue.Undefined;
            }
        }
    }
}
```

**Example: `a + b * c`**

```
LoadLocal(0)      // stack: [a]
LoadLocal(1)      // stack: [a, b]
LoadLocal(2)      // stack: [a, b, c]
Multiply()        // stack: [a, (b*c)]
Add()             // stack: [(a+b*c)]
```

### Register Machine

Faster (fewer stack operations), what V8 Ignition and LuaJIT use:

```csharp
internal sealed class RegisterMachineInterpreter
{
    private readonly JsValue[] _registers = new JsValue[256];
    private int _pc;
    private readonly RegisterInstruction[] _code;

    public JsValue Execute()
    {
        while (true)
        {
            var instruction = _code[_pc++];
            switch (instruction)
            {
                case LoadIntInstruction load:
                    _registers[load.Dest] = load.Value;
                    break;

                case AddInstruction add:
                    _registers[add.Dest] = JsValue.Add(
                        _registers[add.Left],
                        _registers[add.Right]);
                    break;

                case BranchIfFalseInstruction branch:
                    if (!_registers[branch.Cond].ToBoolean())
                        _pc = branch.Target;
                    break;

                case ReturnInstruction ret:
                    return _registers[ret.Value];
            }
        }
    }
}

// Instructions reference registers
record LoadIntInstruction(int Dest, int Value);
record AddInstruction(int Dest, int Left, int Right);
record BranchIfFalseInstruction(int Cond, int Target);
record ReturnInstruction(int Value);
```

**Example: `a + b * c`**

```
// a is in r0, b in r1, c in r2
Multiply(r3, r1, r2)   // r3 = b * c
Add(r0, r0, r3)        // r0 = a + (b * c)
Return(r0)
```

### Comparison

| Aspect | Stack Machine | Register Machine |
|--------|--------------|------------------|
| Code size | Smaller (implicit operands) | Larger (explicit registers) |
| Dispatch overhead | Higher (more instructions) | Lower (fewer instructions) |
| Implementation | Simpler | More complex |
| Optimization | Harder | Easier (SSA-like) |
| Real-world | JVM, CPython, .NET | V8 Ignition, LuaJIT |

**Recommendation**: Start with stack machine for simplicity, optimize to register later if needed.

## Scope Analysis

Currently, variable lookup walks the environment chain at runtime. Bytecode needs compile-time resolution.

### Current Runtime Lookup

```csharp
// At runtime, for each variable access
JsValue LookupVariable(Symbol name, JsEnvironment env)
{
    while (env != null)
    {
        if (env.HasBinding(name))
            return env.GetBinding(name);
        env = env.Parent;
    }
    return global.GetProperty(name);
}
```

### Bytecode: Compile-Time Resolution

```csharp
// During compilation, resolve to slot index
class ScopeAnalyzer
{
    record Binding(int Depth, int SlotIndex, bool IsCaptured);

    Binding? ResolveVariable(Symbol name)
    {
        var depth = 0;
        var scope = _currentScope;

        while (scope != null)
        {
            if (scope.TryGetSlot(name, out var slot))
            {
                return new Binding(depth, slot, depth > 0);
            }
            scope = scope.Parent;
            depth++;
        }

        return null;  // Must be global
    }
}
```

**Output:**
- `depth == 0` → `LoadLocalInstruction(slot)`
- `depth > 0` → `LoadClosureInstruction(upvalueIndex)`
- `null` → `LoadGlobalInstruction(name)`

### Upvalues for Closures

When a variable is captured by a closure:

```javascript
function outer() {
    let x = 1;              // Slot 0, but captured!
    return function inner() {
        return x;           // Upvalue 0 → outer's slot 0
    };
}
```

**Compilation:**

```
outer's bytecode:
  [0] PushInt(1)
  [1] StoreLocal(0)              // x = 1
  [2] CreateFunction(inner, upvalues=[LocalUpvalue(0)])
  [3] Return()

inner's bytecode:
  [0] LoadClosure(0)             // Load upvalue 0
  [1] Return()
```

**Runtime upvalue structure:**

```csharp
abstract record Upvalue;
record OpenUpvalue(JsValue[] Locals, int SlotIndex) : Upvalue;  // Still on stack
record ClosedUpvalue(JsValue Value) : Upvalue;                   // Heap allocated

// When outer() returns, open upvalues are "closed":
// OpenUpvalue(locals, 0) → ClosedUpvalue(locals[0])
```

## Call Frames

With bytecode, we need explicit call frames:

```csharp
internal sealed class CallFrame
{
    public FunctionBytecode Function { get; }
    public JsValue[] Locals { get; }
    public Upvalue[] Upvalues { get; }
    public int ProgramCounter { get; set; }
    public int StackBase { get; }      // Where our stack slots start
    public JsValue ThisBinding { get; }
}

internal sealed class VirtualMachine
{
    private readonly Stack<CallFrame> _callStack = new();
    private readonly JsValue[] _stack = new JsValue[1024];
    private int _sp;

    private void Call(FunctionBytecode callee, int argCount)
    {
        // Save current frame state
        _currentFrame.ProgramCounter = _pc;

        // Create new frame
        var frame = new CallFrame
        {
            Function = callee,
            Locals = new JsValue[callee.LocalCount],
            StackBase = _sp - argCount,
            // ... copy args to locals
        };

        _callStack.Push(frame);
        _currentFrame = frame;
        _pc = 0;
    }

    private JsValue Return(JsValue value)
    {
        _callStack.Pop();
        if (_callStack.Count == 0)
            return value;  // Done

        _currentFrame = _callStack.Peek();
        _pc = _currentFrame.ProgramCounter;
        _sp = _currentFrame.StackBase;
        _stack[_sp++] = value;  // Push return value
        // Continue execution
    }
}
```

## Benefits of Full Bytecode

### 1. Single Execution Model

No split between sync and async paths:

```
Current:
  if (isGenerator) → IR interpreter
  else → AST interpreter

Bytecode:
  All code → Bytecode interpreter
```

### 2. Faster Dispatch

AST interpretation requires type checks on every node:

```csharp
// Current: many virtual calls and type checks
JsValue Evaluate(ExpressionNode node) => node switch
{
    BinaryExpression b => EvaluateBinary(b),
    CallExpression c => EvaluateCall(c),
    MemberExpression m => EvaluateMember(m),
    // ... 50+ cases
};
```

Bytecode: tight loop with fewer types:

```csharp
// Bytecode: simpler dispatch
switch (_code[_pc++])
{
    case Opcode.Add: ...
    case Opcode.Call: ...
    // Flat, no nesting
}
```

### 3. Optimization Opportunities

**Peephole optimization:**
```
// Before
LoadLocal(0)
LoadLocal(0)
Add()

// After (common subexpression)
LoadLocal(0)
Dup()
Add()
```

**Constant folding:**
```
// Before
PushInt(2)
PushInt(3)
Add()

// After
PushInt(5)
```

### 4. Serialization

Can cache compiled bytecode:

```csharp
// First run
var bytecode = Compile(source);
SaveToCache(hash(source), bytecode);

// Subsequent runs
if (TryLoadFromCache(hash(source), out var cached))
    return cached;  // Skip parsing and compilation
```

### 5. Debugging

Bytecode positions map cleanly to source:

```csharp
record BytecodeFunction
{
    ImmutableArray<Instruction> Code { get; }
    ImmutableArray<SourceMapping> SourceMap { get; }  // bytecode offset → source position
}
```

## Challenges

### 1. `eval()` and Dynamic Scoping

`eval()` can create variables dynamically:

```javascript
function foo(code) {
    let x = 1;
    eval(code);  // Could do: var y = 2
    return y;    // Is 'y' defined? Don't know at compile time!
}
```

**Solution**: Detect `eval` usage and deoptimize:

```csharp
if (functionContainsEval)
{
    // Use dynamic lookup for all variables in this function
    emit(LoadDynamicInstruction(name));
}
else
{
    // Safe to use static slots
    emit(LoadLocalInstruction(slot));
}
```

### 2. `with` Statement

`with` adds dynamic scope:

```javascript
with (obj) {
    x = 1;  // obj.x = 1? Or outer x = 1? Runtime decision!
}
```

**Solution**: Similar deoptimization or prohibit in strict mode (which ES5+ does).

### 3. `arguments` Object

The `arguments` object aliases parameters:

```javascript
function foo(a) {
    arguments[0] = 10;
    return a;  // Returns 10 in sloppy mode!
}
```

**Solution**: Detect `arguments` usage and either:
- Create full arguments object (slow path)
- Use direct parameter access if no aliasing (fast path)

### 4. Exception Stack Unwinding

With bytecode, must track what's on the stack at each point:

```javascript
try {
    result = foo(bar(), baz());
    //           ^      ^ throw here - what's on stack?
} catch (e) { }
```

**Solution**: Exception table mapping PC ranges to handlers + stack depth:

```csharp
record ExceptionHandler(
    int StartPC,
    int EndPC,
    int HandlerPC,
    int StackDepth,    // Expected stack depth when entering try
    Symbol? CatchBinding);
```

### 5. Performance of Type Checks

JavaScript values are dynamically typed:

```csharp
case AddInstruction:
    var right = _stack[--_sp];
    var left = _stack[--_sp];
    // Must check types at runtime!
    if (left.IsNumber && right.IsNumber)
        _stack[_sp++] = left.AsNumber + right.AsNumber;
    else
        _stack[_sp++] = JsValue.Add(left, right);  // Complex path
```

**Future optimization**: Inline caching, type feedback, speculative optimization.

## Incremental Migration Path

Don't have to do everything at once:

### Phase 1: Expression Bytecode

Keep current control flow IR, compile expressions to bytecode:

```csharp
// Current
record BranchInstruction(ExpressionNode Condition, int Then, int Else);

// Phase 1
record BranchInstruction(int ConditionBytecodeStart, int Then, int Else);
// Condition is now bytecode that leaves bool on stack
```

### Phase 2: Scope Analysis

Add compile-time variable resolution:

- Build scope tree during parsing
- Resolve identifiers to slots
- Detect captured variables for closures

### Phase 3: Unified Execution

All code goes through bytecode:

- Remove AST interpreter
- Single `VirtualMachine.Execute()` entry point
- Sync and async use same bytecode, async just has yield/await instructions

### Phase 4: Closure Optimization

Proper upvalue handling:

- Open/closed upvalue distinction
- Close upvalues on scope exit
- Flat closure optimization (copy values when possible)

### Phase 5: Inline Caching

Property access optimization:

```csharp
// Monomorphic inline cache
record GetPropertyInstruction(Symbol Name)
{
    // Cached from previous execution
    internal Shape? CachedShape;
    internal int CachedOffset;
}

// Execution
if (obj.Shape == instruction.CachedShape)
{
    // Fast path: direct slot access
    return obj.Slots[instruction.CachedOffset];
}
else
{
    // Slow path: full lookup, update cache
    var (shape, offset) = obj.LookupProperty(name);
    instruction.CachedShape = shape;
    instruction.CachedOffset = offset;
    return obj.Slots[offset];
}
```

## Example: Full Compilation

### Input

```javascript
function add(a, b) {
    return a + b;
}

function sumArray(arr) {
    let total = 0;
    for (let i = 0; i < arr.length; i++) {
        total = total + arr[i];
    }
    return total;
}
```

### Current IR (only for generators)

```
add: (not compiled to IR, direct AST interpretation)

sumArray: (not compiled to IR, direct AST interpretation)
```

### Full Bytecode

```
add (locals: [a, b], args: 2):
  [0] LoadLocal(0)           // a
  [1] LoadLocal(1)           // b
  [2] Add()
  [3] Return()

sumArray (locals: [arr, total, i], args: 1):
  [0]  PushInt(0)
  [1]  StoreLocal(1)         // total = 0
  [2]  PushInt(0)
  [3]  StoreLocal(2)         // i = 0
  [4]  LoadLocal(2)          // loop start: i
  [5]  LoadLocal(0)          // arr
  [6]  GetProperty(length)   // arr.length
  [7]  LessThan()            // i < arr.length
  [8]  BranchIfFalse(18)     // exit loop
  [9]  LoadLocal(1)          // total
  [10] LoadLocal(0)          // arr
  [11] LoadLocal(2)          // i
  [12] GetComputedProperty() // arr[i]
  [13] Add()                 // total + arr[i]
  [14] StoreLocal(1)         // total = ...
  [15] LoadLocal(2)          // i
  [16] Increment()           // i + 1
  [17] StoreLocal(2)         // i = ...
  [18] Jump(4)               // back to loop start
  [19] LoadLocal(1)          // total
  [20] Return()
```

## Relation to Current IR

The current generator IR is **70% of the way there**:

| Have | Need |
|------|------|
| Control flow instructions | ✓ Already have |
| Jump/branch targets | ✓ Already have |
| Program counter execution | ✓ Already have |
| Yield/await handling | ✓ Already have |
| Expression compilation | ✗ Need to add |
| Static scope resolution | ✗ Need to add |
| Upvalue handling | ✗ Need to add |
| Call frame management | ✗ Need to add |

The architectural foundation exists. Extending to full bytecode is incremental work, not a rewrite.

## File Reference

| Current Component | File | Bytecode Equivalent |
|-------------------|------|---------------------|
| IR instructions | `src/Asynkron.JsEngine/Execution/GeneratorIr.cs` | Extend with expression ops |
| IR builder | `src/Asynkron.JsEngine/Execution/SyncGeneratorIrBuilder.cs` | Extend to compile all code |
| IR interpreter | `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedGeneratorInstance.cs` | Generalize to VM |
| AST evaluator | `src/Asynkron.JsEngine/Ast/*Extensions.cs` | Replace with bytecode |
| Environments | `src/Asynkron.JsEngine/JsEnvironment.cs` | Replace with slots + upvalues |

## Summary

Converting to full bytecode means:

1. **Compile expressions** to stack/register instructions (not just control flow)
2. **Static scope analysis** to resolve variables at compile time
3. **Upvalue mechanism** for closures
4. **Call frame stack** for function calls
5. **Single execution path** for all code (sync, async, generators)

The current IR infrastructure provides the foundation. The main work is:
- Expression instruction set
- Scope analyzer
- Closure/upvalue handling
- Removing AST interpretation fallback

This follows the same path that V8, SpiderMonkey, and other production engines took.
