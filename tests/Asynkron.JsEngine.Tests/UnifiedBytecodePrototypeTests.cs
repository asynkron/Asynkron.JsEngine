using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.Debugging)]
public sealed class UnifiedBytecodePrototypeTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public void TryCompile_SimpleReturnAdd_ProducesUnifiedOps()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function add(x, y) {
                return x + y;
            }
            """,
            "add");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Equal(4, program.Instructions.Length);
        Assert.Equal(UnifiedBytecodeOpCode.LoadSlot, program.Instructions[0].OpCode);
        Assert.Equal(UnifiedBytecodeOpCode.LoadSlot, program.Instructions[1].OpCode);
        Assert.Equal(UnifiedBytecodeOpCode.Binary, program.Instructions[2].OpCode);
        Assert.Equal((int)BinaryOperator.Add, program.Instructions[2].Operand);
        Assert.Equal(UnifiedBytecodeOpCode.Return, program.Instructions[3].OpCode);
    }

    [Fact]
    public void TryCompile_DirectNamedPropertyRead_ProducesOwnedPropertyOp()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function read(box) {
                return box.value;
            }
            """,
            "read");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Equal(3, program.Instructions.Length);
        Assert.Equal(UnifiedBytecodeOpCode.LoadSlot, program.Instructions[0].OpCode);
        Assert.Equal(UnifiedBytecodeOpCode.GetNamedProperty, program.Instructions[1].OpCode);
        Assert.Equal(0, program.Instructions[1].Operand);
        Assert.Equal("value", Assert.Single(program.StringConstants));
        Assert.Equal(UnifiedBytecodeOpCode.Return, program.Instructions[2].OpCode);
    }

    [Fact]
    public void TryCompile_TwoHopNamedPropertyRead_ProducesOwnedPropertyOps()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function read(box) {
                return box.child.value;
            }
            """,
            "read");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Equal(4, program.Instructions.Length);
        Assert.Equal(UnifiedBytecodeOpCode.LoadSlot, program.Instructions[0].OpCode);
        Assert.Equal(UnifiedBytecodeOpCode.GetNamedProperty, program.Instructions[1].OpCode);
        Assert.Equal(UnifiedBytecodeOpCode.GetNamedProperty, program.Instructions[2].OpCode);
        Assert.Equal(UnifiedBytecodeOpCode.Return, program.Instructions[3].OpCode);
        Assert.Equal(new[] { "child", "value" }, program.StringConstants);
    }

    [Fact]
    public void Execute_DirectNamedPropertyRead_ReturnsObjectPropertyValue()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function read(box) {
                return box.value;
            }
            """,
            "read");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);

        var box = new JsObject();
        box.SetProperty("value", JsValue.FromDouble(42));
        var slots = new JsValue[Math.Max(program.SlotCount, 1)];
        SetSlot(program, slots, "box", JsValue.FromJsObject(box));

        Assert.Equal(42d, ExecuteProgram(program, slots).AsDouble());
    }

    [Fact]
    public void Execute_BreakThroughFinally_RunsFinallyBeforeTarget()
    {
        var program = CreateAbruptThroughFinallyProgram(UnifiedBytecodeOpCode.Break);
        var slots = new[] { JsValue.Undefined };

        var result = ExecuteProgram(program, slots);

        Assert.Equal(11d, result.AsDouble());
    }

    [Fact]
    public void ExecuteResumable_YieldAndResumeValue_PreservesProgramCounterStackAndSlots()
    {
        var program = CreateResumableYieldReturnProgram();
        var slots = new[] { JsValue.Undefined };
        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var state = new UnifiedBytecodeResumeState(program, slots);

        var first = UnifiedBytecodeVirtualMachine.ExecuteResumable(
            state,
            UnifiedBytecodeResumeMode.Next,
            JsValue.Undefined,
            context);

        Assert.Equal(UnifiedBytecodeStepKind.Yield, first.Kind);
        Assert.Equal(10d, first.Value.AsDouble());
        Assert.False(first.Done);
        Assert.Equal(2, state.ProgramCounter);
        Assert.Equal(0, state.StackPointer);
        Assert.False(state.IsCompleted);
        Assert.True(slots[0].IsUndefined);

        var second = UnifiedBytecodeVirtualMachine.ExecuteResumable(
            state,
            UnifiedBytecodeResumeMode.Next,
            JsValue.FromDouble(41),
            context);

        Assert.Equal(UnifiedBytecodeStepKind.Completed, second.Kind);
        Assert.Equal(42d, second.Value.AsDouble());
        Assert.True(second.Done);
        Assert.True(state.IsCompleted);
        Assert.Equal(41d, slots[0].AsDouble());
    }

    [Fact]
    public void ExecuteResumable_AwaitedReturn_PreservesPendingAwaitAndCompletesWithResumeValue()
    {
        var program = new UnifiedBytecodeProgram(
            ImmutableArray.Create(
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.AwaitedReturn)),
            MaxStackDepth: 1,
            SlotCount: 0,
            LiteralConstants: ImmutableArray.Create(JsValue.FromDouble(10)),
            StringConstants: ImmutableArray<string>.Empty,
            SlotNames: ImmutableArray<string?>.Empty,
            ParameterSlotIndices: ImmutableArray<int>.Empty,
            LexicalSlotIndices: ImmutableArray<int>.Empty,
            CallTargetConstants: ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ScopeDescriptors: ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            TryDescriptors: ImmutableArray<UnifiedBytecodeTryDescriptor>.Empty,
            CatchDescriptors: ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            DriverDescriptors: ImmutableArray<UnifiedBytecodeDriverDescriptor>.Empty);
        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var state = new UnifiedBytecodeResumeState(program, []);

        var first = UnifiedBytecodeVirtualMachine.ExecuteResumable(
            state,
            UnifiedBytecodeResumeMode.Next,
            JsValue.Undefined,
            context);

        Assert.Equal(UnifiedBytecodeStepKind.PendingAwait, first.Kind);
        Assert.False(state.PendingAwaitPromise.IsUndefined);
        Assert.Equal(1, state.ProgramCounter);

        var second = UnifiedBytecodeVirtualMachine.ExecuteResumable(
            state,
            UnifiedBytecodeResumeMode.Next,
            JsValue.FromDouble(42),
            context);

        Assert.Equal(UnifiedBytecodeStepKind.Completed, second.Kind);
        Assert.Equal(42d, second.Value.AsDouble());
        Assert.True(state.IsCompleted);
    }

    [Fact]
    public void ExecuteResumable_AwaitAndDiscard_ResumesAfterPendingAwait()
    {
        var program = new UnifiedBytecodeProgram(
            ImmutableArray.Create(
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.AwaitAndDiscard),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, 1),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return)),
            MaxStackDepth: 1,
            SlotCount: 0,
            LiteralConstants: ImmutableArray.Create(JsValue.FromDouble(10), JsValue.FromDouble(7)),
            StringConstants: ImmutableArray<string>.Empty,
            SlotNames: ImmutableArray<string?>.Empty,
            ParameterSlotIndices: ImmutableArray<int>.Empty,
            LexicalSlotIndices: ImmutableArray<int>.Empty,
            CallTargetConstants: ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ScopeDescriptors: ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            TryDescriptors: ImmutableArray<UnifiedBytecodeTryDescriptor>.Empty,
            CatchDescriptors: ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            DriverDescriptors: ImmutableArray<UnifiedBytecodeDriverDescriptor>.Empty);
        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var state = new UnifiedBytecodeResumeState(program, []);

        var first = UnifiedBytecodeVirtualMachine.ExecuteResumable(
            state,
            UnifiedBytecodeResumeMode.Next,
            JsValue.Undefined,
            context);
        var second = UnifiedBytecodeVirtualMachine.ExecuteResumable(
            state,
            UnifiedBytecodeResumeMode.Next,
            JsValue.FromDouble(41),
            context);

        Assert.Equal(UnifiedBytecodeStepKind.PendingAwait, first.Kind);
        Assert.Equal(UnifiedBytecodeStepKind.Completed, second.Kind);
        Assert.Equal(7d, second.Value.AsDouble());
    }

    [Fact]
    public void ExecuteResumable_PendingAbruptCompletion_SurvivesSuspensionAndResume()
    {
        var program = new UnifiedBytecodeProgram(
            ImmutableArray.Create(
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Yield),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ReturnUndefined)),
            MaxStackDepth: 1,
            SlotCount: 0,
            LiteralConstants: ImmutableArray.Create(JsValue.FromDouble(10)),
            StringConstants: ImmutableArray<string>.Empty,
            SlotNames: ImmutableArray<string?>.Empty,
            ParameterSlotIndices: ImmutableArray<int>.Empty,
            LexicalSlotIndices: ImmutableArray<int>.Empty,
            CallTargetConstants: ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ScopeDescriptors: ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            TryDescriptors: ImmutableArray<UnifiedBytecodeTryDescriptor>.Empty,
            CatchDescriptors: ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            DriverDescriptors: ImmutableArray<UnifiedBytecodeDriverDescriptor>.Empty);
        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var state = new UnifiedBytecodeResumeState(program, []);

        var first = UnifiedBytecodeVirtualMachine.ExecuteResumable(
            state,
            UnifiedBytecodeResumeMode.Next,
            JsValue.Undefined,
            context);
        state.PendingAbruptCompletion = new UnifiedBytecodePendingAbruptCompletion(
            UnifiedBytecodeAbruptCompletionKind.Return,
            JsValue.FromDouble(42),
            Target: -1,
            ResumeTarget: 2,
            OriginatedInFinally: true);
        var second = UnifiedBytecodeVirtualMachine.ExecuteResumable(
            state,
            UnifiedBytecodeResumeMode.Next,
            JsValue.Undefined,
            context);

        Assert.Equal(UnifiedBytecodeStepKind.Yield, first.Kind);
        Assert.Equal(UnifiedBytecodeStepKind.Completed, second.Kind);
        Assert.Equal(42d, second.Value.AsDouble());
        Assert.Equal(UnifiedBytecodeAbruptCompletionKind.None, state.PendingAbruptCompletion.Kind);
    }

    [Fact]
    public void TryCompile_SimpleGeneratorYieldSend_ProducesResumableOps()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function* gen(input) {
                var x = yield input;
                return x + 1;
            }
            """,
            "gen");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Contains(program.Instructions, static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Yield);
        Assert.Contains(
            program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.StoreResumeValue);
    }

    [Fact]
    public void TryCompile_AsyncAwaitedReturnAndAwaitDiscard_ProducesResumableAwaitOps()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            async function run(input) {
                await input;
                return await 41;
            }
            """,
            "run");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Contains(
            program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.AwaitAndDiscard);
        Assert.Contains(
            program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.AwaitedReturn);
    }

    [Fact]
    public void TryCompile_SyncGeneratorYieldStar_ProducesResumableYieldStarOp()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function* relay(values) {
                yield* values;
            }
            """,
            "relay");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Contains(
            program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.YieldStar);
        Assert.NotEmpty(program.DriverDescriptors);
    }

    [Fact]
    public void ExecuteResumable_YieldStar_DelegatesSyncIterable()
    {
        var program = new UnifiedBytecodeProgram(
            ImmutableArray.Create(
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.YieldStar, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ReturnUndefined)),
            MaxStackDepth: 1,
            SlotCount: 2,
            LiteralConstants: ImmutableArray<JsValue>.Empty,
            StringConstants: ImmutableArray<string>.Empty,
            SlotNames: ImmutableArray.Create<string?>("values", "state"),
            ParameterSlotIndices: ImmutableArray<int>.Empty,
            LexicalSlotIndices: ImmutableArray<int>.Empty,
            CallTargetConstants: ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ScopeDescriptors: ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            TryDescriptors: ImmutableArray<UnifiedBytecodeTryDescriptor>.Empty,
            CatchDescriptors: ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            DriverDescriptors: ImmutableArray.Create(new UnifiedBytecodeDriverDescriptor(StateSlot: 1)));
        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var slots = new[] { JsValue.FromJsObject(CreateSingleValueIterable(JsValue.FromDouble(9), () => { })), JsValue.Undefined };
        var state = new UnifiedBytecodeResumeState(program, slots);

        var first = UnifiedBytecodeVirtualMachine.ExecuteResumable(
            state,
            UnifiedBytecodeResumeMode.Next,
            JsValue.Undefined,
            context);
        var second = UnifiedBytecodeVirtualMachine.ExecuteResumable(
            state,
            UnifiedBytecodeResumeMode.Next,
            JsValue.Undefined,
            context);

        Assert.Equal(UnifiedBytecodeStepKind.Yield, first.Kind);
        Assert.Equal(9d, first.Value.AsDouble());
        Assert.Equal(UnifiedBytecodeStepKind.Completed, second.Kind);
        Assert.True(second.Value.IsUndefined);
    }

    [Fact]
    public void ExecuteResumable_YieldStar_ForwardsResumePayloadToDelegateNext()
    {
        var program = new UnifiedBytecodeProgram(
            ImmutableArray.Create(
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.YieldStar, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 2),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return)),
            MaxStackDepth: 1,
            SlotCount: 3,
            LiteralConstants: ImmutableArray<JsValue>.Empty,
            StringConstants: ImmutableArray<string>.Empty,
            SlotNames: ImmutableArray.Create<string?>("values", "state", "result"),
            ParameterSlotIndices: ImmutableArray<int>.Empty,
            LexicalSlotIndices: ImmutableArray<int>.Empty,
            CallTargetConstants: ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ScopeDescriptors: ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            TryDescriptors: ImmutableArray<UnifiedBytecodeTryDescriptor>.Empty,
            CatchDescriptors: ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            DriverDescriptors: ImmutableArray.Create(new UnifiedBytecodeDriverDescriptor(
                StateSlot: 1,
                ValueSlot: 2)));
        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var received = new List<JsValue>();
        var slots = new[]
        {
            JsValue.FromJsObject(CreateSendRecordingIterable(JsValue.FromDouble(9), received)),
            JsValue.Undefined,
            JsValue.Undefined
        };
        var state = new UnifiedBytecodeResumeState(program, slots);

        var first = UnifiedBytecodeVirtualMachine.ExecuteResumable(
            state,
            UnifiedBytecodeResumeMode.Next,
            JsValue.Undefined,
            context);
        var second = UnifiedBytecodeVirtualMachine.ExecuteResumable(
            state,
            UnifiedBytecodeResumeMode.Next,
            JsValue.FromDouble(42),
            context);

        Assert.Equal(UnifiedBytecodeStepKind.Yield, first.Kind);
        Assert.Equal(9d, first.Value.AsDouble());
        Assert.Equal(UnifiedBytecodeStepKind.Completed, second.Kind);
        Assert.Equal(42d, second.Value.AsDouble());
        Assert.Equal(42d, slots[2].AsDouble());
        Assert.Equal(2, received.Count);
        Assert.True(received[0].IsUndefined);
        Assert.Equal(42d, received[1].AsDouble());
    }

    [Fact]
    public void Execute_ContinueThroughFinally_RunsFinallyBeforeTarget()
    {
        var program = CreateAbruptThroughFinallyProgram(UnifiedBytecodeOpCode.Continue);
        var slots = new[] { JsValue.Undefined };

        var result = ExecuteProgram(program, slots);

        Assert.Equal(11d, result.AsDouble());
    }

    [Fact]
    public void TryCompile_DirectComputedPropertyRead_ProducesOrderedPropertyOps()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function read(box, key) {
                return box[key];
            }
            """,
            "read");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Equal(
            new[]
            {
                UnifiedBytecodeOpCode.LoadSlot,
                UnifiedBytecodeOpCode.LoadSlot,
                UnifiedBytecodeOpCode.RequireObjectCoercible,
                UnifiedBytecodeOpCode.ResolvePropertyKey,
                UnifiedBytecodeOpCode.GetComputedProperty,
                UnifiedBytecodeOpCode.Return
            },
            program.Instructions.Select(instruction => instruction.OpCode).ToArray());
        Assert.Equal(1, program.Instructions[2].Operand);
    }

    [Fact]
    public void TryCompile_DirectIdentifierCall_ProducesCallTargetPrepBoundary()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function invoke(helper, value) {
                return helper(value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Equal(
            new[]
            {
                UnifiedBytecodeOpCode.PrepareIdentifierCallTarget,
                UnifiedBytecodeOpCode.LoadSlot,
                UnifiedBytecodeOpCode.CallInvocationBoundary,
                UnifiedBytecodeOpCode.Return
            },
            program.Instructions.Select(instruction => instruction.OpCode).ToArray());
        var callTarget = Assert.Single(program.CallTargetConstants);
        Assert.Equal(UnifiedBytecodeCallTargetKind.Identifier, callTarget.Kind);
        Assert.Equal("helper", program.StringConstants[callTarget.NameConstantIndex]);
        Assert.Equal(1, program.Instructions[2].Operand);
    }

    [Fact]
    public void TryCompile_NamedMemberCall_ProducesCallTargetPrepBoundary()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function invoke(box, value) {
                return box.read(value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Equal(
            new[]
            {
                UnifiedBytecodeOpCode.LoadSlot,
                UnifiedBytecodeOpCode.PrepareNamedCallTarget,
                UnifiedBytecodeOpCode.LoadSlot,
                UnifiedBytecodeOpCode.CallInvocationBoundary,
                UnifiedBytecodeOpCode.Return
            },
            program.Instructions.Select(instruction => instruction.OpCode).ToArray());
        var callTarget = Assert.Single(program.CallTargetConstants);
        Assert.Equal(UnifiedBytecodeCallTargetKind.NamedMember, callTarget.Kind);
        Assert.Equal("read", program.StringConstants[callTarget.NameConstantIndex]);
        Assert.Equal(1, program.Instructions[3].Operand);
    }

    [Fact]
    public void TryCompile_ComputedMemberCall_ProducesCallTargetPrepBoundary()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function invoke(box, key, value) {
                return box[key](value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Equal(
            new[]
            {
                UnifiedBytecodeOpCode.LoadSlot,
                UnifiedBytecodeOpCode.LoadSlot,
                UnifiedBytecodeOpCode.PrepareComputedCallTarget,
                UnifiedBytecodeOpCode.LoadSlot,
                UnifiedBytecodeOpCode.CallInvocationBoundary,
                UnifiedBytecodeOpCode.Return
            },
            program.Instructions.Select(instruction => instruction.OpCode).ToArray());
        var callTarget = Assert.Single(program.CallTargetConstants);
        Assert.Equal(UnifiedBytecodeCallTargetKind.ComputedMember, callTarget.Kind);
        Assert.Equal(1, program.Instructions[4].Operand);
    }

    [Fact]
    public void Execute_DirectComputedPropertyRead_ResolvesLiteralKeyAndReadsValue()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function read(box) {
                return box["value"];
            }
            """,
            "read");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);
        Assert.Single(program.LiteralConstants);

        var box = new JsObject();
        box.SetProperty("value", JsValue.FromDouble(7));
        var slots = new JsValue[Math.Max(program.SlotCount, 1)];
        SetSlot(program, slots, "box", JsValue.FromJsObject(box));

        Assert.Equal(7d, ExecuteProgram(program, slots).AsDouble());
    }

    [Fact]
    public void TryCompile_ArrayLiteralWithHoleAndNestedLiteral_ProducesLiteralConstructionOps()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function create(x) {
                return [1, , [x]];
            }
            """,
            "create");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.CreateArray);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayPush);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayPushHole);

        var slots = new JsValue[Math.Max(program.SlotCount, 1)];
        SetSlot(program, slots, "x", JsValue.FromDouble(7));
        var value = ExecuteProgram(program, slots);

        Assert.True(value.TryGetArray(out var array));
        Assert.Equal(3, array.Length);
        Assert.True(array.TryGetProperty("0", out var first));
        Assert.Equal(1d, first.AsDouble());
        Assert.False(array.TryGetProperty("1", out _));
        Assert.True(array.TryGetProperty("2", out var nestedValue));
        Assert.True(nestedValue.TryGetArray(out var nestedArray));
        Assert.True(nestedArray.TryGetProperty("0", out var nestedFirst));
        Assert.Equal(7d, nestedFirst.AsDouble());
    }

    [Fact]
    public void TryCompile_ObjectLiteralWithStaticComputedAndPrototypeMutation_ExecutesLiteralConstructionOps()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function create(key, proto) {
                return { __proto__: proto, a: 1, a: 2, [key]: 3 };
            }
            """,
            "create");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.CreateObject);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);

        var proto = new JsObject();
        proto.SetProperty("inherited", JsValue.FromDouble(9));
        var slots = new JsValue[Math.Max(program.SlotCount, 2)];
        SetSlot(program, slots, "key", new JsValue("b"));
        SetSlot(program, slots, "proto", JsValue.FromJsObject(proto));
        var value = ExecuteProgram(program, slots);

        Assert.True(value.TryGetObject(out var obj));
        Assert.True(obj.TryGetProperty("a", out var a));
        Assert.Equal(2d, a.AsDouble());
        Assert.True(obj.TryGetProperty("b", out var b));
        Assert.Equal(3d, b.AsDouble());
        Assert.True(obj.TryGetProperty("inherited", out var inherited));
        Assert.Equal(9d, inherited.AsDouble());
    }

    [Fact]
    public void TryCompile_ObjectLiteralWithSimpleSpread_ProducesObjectSpreadOp()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function create(source) {
                return { a: 1, ...source, b: 2 };
            }
            """,
            "create");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.ObjectSpread);

        var source = new JsObject();
        source.DefineDefaultDataProperty("a", JsValue.FromDouble(7));
        source.DefineDefaultDataProperty("c", JsValue.FromDouble(33));
        var slots = new JsValue[Math.Max(program.SlotCount, 1)];
        SetSlot(program, slots, "source", JsValue.FromJsObject(source));
        var value = ExecuteProgram(program, slots);

        Assert.True(value.TryGetObject(out var obj));
        Assert.True(obj.TryGetProperty("a", out var a));
        Assert.Equal(7d, a.AsDouble());
        Assert.True(obj.TryGetProperty("b", out var b));
        Assert.Equal(2d, b.AsDouble());
        Assert.True(obj.TryGetProperty("c", out var c));
        Assert.Equal(33d, c.AsDouble());
    }

    [Fact]
    public void TryCompile_ComputedPropertyReadWithExpressionKey_ProducesGeneralPropertyOps()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function read(box, left, right) {
                return box[left + right];
            }
            """,
            "read");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Equal(
            new[]
            {
                UnifiedBytecodeOpCode.LoadSlot,
                UnifiedBytecodeOpCode.LoadSlot,
                UnifiedBytecodeOpCode.LoadSlot,
                UnifiedBytecodeOpCode.Binary,
                UnifiedBytecodeOpCode.RequireObjectCoercible,
                UnifiedBytecodeOpCode.ResolvePropertyKey,
                UnifiedBytecodeOpCode.GetComputedProperty,
                UnifiedBytecodeOpCode.Return
            },
            program.Instructions.Select(instruction => instruction.OpCode).ToArray());
        Assert.Equal(1, program.Instructions[4].Operand);
    }

    [Fact]
    public void Execute_AddProgram_ReturnsFiveForTwoAndThree()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function add(x, y) {
                return x + y;
            }
            """,
            "add");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);

        var slotCount = Math.Max(program.SlotCount, 2);
        var slots = new JsValue[slotCount];
        slots[program.Instructions[0].Operand] = JsValue.FromDouble(2);
        slots[program.Instructions[1].Operand] = JsValue.FromDouble(3);

        var value = ExecuteProgram(program, slots);
        Assert.Equal(5d, value.AsDouble());
    }

    [Fact]
    public void TryCompile_LocalDeclarationBeforeReturn_ProducesLinearProgram()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function addViaLocal(a, b) {
                var c = a + b;
                return c;
            }
            """,
            "addViaLocal");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Equal(6, program.Instructions.Length);
        Assert.Equal(UnifiedBytecodeOpCode.LoadSlot, program.Instructions[0].OpCode);
        Assert.Equal(UnifiedBytecodeOpCode.LoadSlot, program.Instructions[1].OpCode);
        Assert.Equal(UnifiedBytecodeOpCode.Binary, program.Instructions[2].OpCode);
        Assert.Equal(UnifiedBytecodeOpCode.InitializeSlot, program.Instructions[3].OpCode);
        Assert.Equal(UnifiedBytecodeOpCode.LoadSlot, program.Instructions[4].OpCode);
        Assert.Equal(UnifiedBytecodeOpCode.Return, program.Instructions[5].OpCode);

        var slots = new JsValue[Math.Max(program.SlotCount, 3)];
        slots[program.Instructions[0].Operand] = JsValue.FromDouble(2);
        slots[program.Instructions[1].Operand] = JsValue.FromDouble(3);
        var value = ExecuteProgram(program, slots);
        Assert.Equal(5d, value.AsDouble());
    }

    [Fact]
    public void TryCompile_MultipleDeclarationsAndLiteral_ProducesLinearProgram()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function linearPack(x, y) {
                var a = x + y;
                var b = a * 2;
                return b;
            }
            """,
            "linearPack");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadLiteral);
        Assert.Equal(1, program.LiteralConstants.Length);
        Assert.Equal(2d, program.LiteralConstants[0].AsDouble());
    }

    [Theory]
    [InlineData("return x + y;", 5d)]
    [InlineData("return x - y;", -1d)]
    [InlineData("return x * y;", 6d)]
    [InlineData("return x / y;", 2d / 3d)]
    [InlineData("return x % y;", 2d)]
    public void Execute_SupportedBinaryOperators_ReturnExpectedValues(string returnStatement, double expected)
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan($$"""
            function op(x, y) {
                {{returnStatement}}
            }
            """,
            "op");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);

        var slots = new JsValue[Math.Max(program.SlotCount, 2)];
        slots[program.Instructions[0].Operand] = JsValue.FromDouble(2);
        slots[program.Instructions[1].Operand] = JsValue.FromDouble(3);

        var value = ExecuteProgram(program, slots);
        Assert.Equal(expected, value.AsDouble(), 12);
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(NumericParityOperators))]
    public async Task Execute_CompiledBinaryOperator_MatchesCurrentRuntimeSemantics(
        string functionName,
        string operatorToken,
        bool returnsBoolean)
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan($$"""
            function {{functionName}}(a, b) {
                return a {{operatorToken}} b;
            }
            """,
            functionName);

        var compileResult = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(compileResult, reason);

        var slots = new JsValue[Math.Max(program.SlotCount, 2)];
        SetSlot(program, slots, "a", JsValue.FromDouble(7));
        SetSlot(program, slots, "b", JsValue.FromDouble(3));
        var vmResult = ExecuteProgram(program, slots);

        await using var engine = CreateEngine();
        var runtimeResult = await engine.Evaluate($$"""
            function {{functionName}}(a, b) {
                return a {{operatorToken}} b;
            }

            {{functionName}}(7, 3);
            """);

        if (returnsBoolean)
        {
            Assert.Equal(Assert.IsType<bool>(runtimeResult), Assert.IsType<bool>(vmResult.ToObject()));
            return;
        }

        Assert.Equal(Assert.IsType<double>(runtimeResult), vmResult.AsDouble(), 12);
    }

    [Fact]
    public void TryCompile_DirectBranchReturn_ProducesJumpProgram()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function min(a, b) {
                if (a < b) {
                    return a;
                }

                return b;
            }
            """,
            "min");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: (int)BinaryOperator.LessThan });

        var slots = new JsValue[Math.Max(program.SlotCount, 2)];
        SetSlot(program, slots, "a", JsValue.FromDouble(2));
        SetSlot(program, slots, "b", JsValue.FromDouble(3));
        Assert.Equal(2d, ExecuteProgram(program, slots).AsDouble());

        SetSlot(program, slots, "a", JsValue.FromDouble(5));
        SetSlot(program, slots, "b", JsValue.FromDouble(3));
        Assert.Equal(3d, ExecuteProgram(program, slots).AsDouble());
    }

    [Fact]
    public void TryCompile_BranchJoinAssignment_ProducesJumpProgram()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function choose(a, b, pick) {
                var c = a + b;

                if (pick) {
                    c = c * 2;
                } else {
                    c = c - 1;
                }

                return c;
            }
            """,
            "choose");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.Jump);

        var slots = new JsValue[Math.Max(program.SlotCount, 4)];
        SetSlot(program, slots, "a", JsValue.FromDouble(2));
        SetSlot(program, slots, "b", JsValue.FromDouble(3));
        SetSlot(program, slots, "pick", JsValue.True);
        Assert.Equal(10d, ExecuteProgram(program, slots).AsDouble());

        SetSlot(program, slots, "a", JsValue.FromDouble(2));
        SetSlot(program, slots, "b", JsValue.FromDouble(3));
        SetSlot(program, slots, "pick", JsValue.False);
        Assert.Equal(4d, ExecuteProgram(program, slots).AsDouble());
    }

    [Fact]
    public void TryCompile_CanonicalWhileLoop_ProducesBackwardJump()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function sumTo(n) {
                var total = 0;
                while (n > 0) {
                    total = total + n;
                    n = n - 1;
                }

                return total;
            }
            """,
            "sumTo");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(
            program.Instructions.Select((instruction, index) => (instruction, index)),
            pair => pair.instruction.OpCode == UnifiedBytecodeOpCode.Jump && pair.instruction.Operand < pair.index);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 10)]
    public void Execute_CanonicalWhileLoop_ReturnsExpectedResult(int n, int expected)
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function sumTo(n) {
                var total = 0;
                while (n > 0) {
                    total = total + n;
                    n = n - 1;
                }

                return total;
            }
            """,
            "sumTo");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);

        var slots = new JsValue[Math.Max(program.SlotCount, 2)];
        SetSlot(program, slots, "n", JsValue.FromDouble(n));
        Assert.Equal(expected, ExecuteProgram(program, slots).AsDouble());
    }

    [Fact]
    public void TryCompile_ConditionOnlyForLoop_ProducesBackwardJump()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function sumTo(n) {
                var total = 0;
                for (; n > 0;) {
                    total = total + n;
                    n = n - 1;
                }

                return total;
            }
            """,
            "sumTo");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(
            program.Instructions.Select((instruction, index) => (instruction, index)),
            pair => pair.instruction.OpCode == UnifiedBytecodeOpCode.Jump && pair.instruction.Operand < pair.index);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 10)]
    public void Execute_ConditionOnlyForLoop_ReturnsExpectedResult(int n, int expected)
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function sumTo(n) {
                var total = 0;
                for (; n > 0;) {
                    total = total + n;
                    n = n - 1;
                }

                return total;
            }
            """,
            "sumTo");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);

        var slots = new JsValue[Math.Max(program.SlotCount, 2)];
        SetSlot(program, slots, "n", JsValue.FromDouble(n));
        Assert.Equal(expected, ExecuteProgram(program, slots).AsDouble());
    }

    [Fact]
    public void TryCompile_ForInDriverLoop_ProducesAndExecutesDriverOpcodes()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function countKeys(obj) {
                var count = 0;
                for (var key in obj) {
                    count = count + 1;
                }

                return count;
            }
            """,
            "countKeys");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.ForInInit);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.ForInMoveNext);

        var obj = new JsObject();
        obj.DefineDefaultDataProperty("a", JsValue.FromDouble(1));
        obj.DefineDefaultDataProperty("b", JsValue.FromDouble(2));
        var slots = new JsValue[Math.Max(program.SlotCount, 2)];
        SetSlot(program, slots, "obj", JsValue.FromJsObject(obj));

        Assert.Equal(2d, ExecuteProgram(program, slots).AsDouble());
    }

    [Fact]
    public void TryCompile_ArrayDestructuringDriver_ProducesAndExecutesDriverOpcodes()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function read(values) {
                var [first, second] = values;
                return first + second;
            }
            """,
            "read");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringInit);
        Assert.Contains(program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringElement);
        Assert.Contains(program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringClose);

        var values = new JsArray();
        values.Push(JsValue.FromDouble(2));
        values.Push(JsValue.FromDouble(5));
        var slots = new JsValue[Math.Max(program.SlotCount, 3)];
        SetSlot(program, slots, "values", JsValue.FromJsArray(values));

        Assert.Equal(7d, ExecuteProgram(program, slots).AsDouble());
    }

    [Fact]
    public void TryCompile_ArrayDestructuringRestDriver_CollectsRemainingValues()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function read(values) {
                var [first, ...rest] = values;
                return first;
            }
            """,
            "read");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringRest);

        var values = new JsArray();
        values.Push(JsValue.FromDouble(3));
        values.Push(JsValue.FromDouble(10));
        values.Push(JsValue.FromDouble(20));
        var slots = new JsValue[Math.Max(program.SlotCount, 3)];
        SetSlot(program, slots, "values", JsValue.FromJsArray(values));

        Assert.Equal(3d, ExecuteProgram(program, slots).AsDouble());
        var restSlotIndex = program.SlotNames.IndexOf("rest");
        Assert.True(restSlotIndex >= 0);
        Assert.True(slots[restSlotIndex].TryGetArray(out var rest));
        Assert.Equal(2, rest.Length);
    }

    [Fact]
    public void TryCompile_ForOfDriverLoop_ProducesAndExecutesIteratorOpcodes()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function sumValues(values) {
                var sum = 0;
                for (var value of values) {
                    sum = sum + value;
                }

                return sum;
            }
            """,
            "sumValues");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);

        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.IteratorInit);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.IteratorMoveNext);

        var values = new JsArray();
        values.Push(JsValue.FromDouble(1));
        values.Push(JsValue.FromDouble(2));
        values.Push(JsValue.FromDouble(3));
        var slots = new JsValue[Math.Max(program.SlotCount, 3)];
        SetSlot(program, slots, "values", JsValue.FromJsArray(values));

        Assert.Equal(6d, ExecuteProgram(program, slots).AsDouble());
    }

    [Fact]
    public void Execute_ForOfBodyGetterThrow_ClosesActiveIteratorAndPreservesBodyThrow()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function read(iterable, box) {
                for (var value of iterable) {
                    return box.value;
                }

                return 0;
            }
            """,
            "read");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);

        var closeCount = 0;
        var iterable = CreateSingleValueIterable(
            JsValue.FromDouble(1),
            onReturn: () =>
            {
                closeCount++;
                throw new ThrowSignal(new JsValue("return boom"));
            });
        var box = new JsObject();
        box.DefineProperty(
            "value",
            new PropertyDescriptor
            {
                Get = new HostFunction((_, _) => throw new ThrowSignal(new JsValue("body boom")), isConstructor: false)
            });
        var slots = new JsValue[Math.Max(program.SlotCount, 3)];
        SetSlot(program, slots, "iterable", JsValue.FromJsObject(iterable));
        SetSlot(program, slots, "box", JsValue.FromJsObject(box));

        var (_, context) = ExecuteProgramWithContext(program, slots);

        Assert.True(context.IsThrow);
        Assert.Equal("body boom", context.FlowValue.AsString());
        Assert.Equal(1, closeCount);
    }

    [Fact]
    public void Execute_ForOfBreak_ClosesIteratorBeforeFollowingCode()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function readAfterBreak(iterable, box) {
                for (var value of iterable) {
                    break;
                }

                return box.value;
            }
            """,
            "readAfterBreak");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.Break);

        var closeCount = 0;
        var iterable = CreateSingleValueIterable(
            JsValue.FromDouble(1),
            onReturn: () => closeCount++);
        var box = new JsObject();
        box.DefineProperty(
            "value",
            new PropertyDescriptor
            {
                Get = new HostFunction((_, _) => JsValue.FromDouble(closeCount), isConstructor: false)
            });
        var slots = new JsValue[Math.Max(program.SlotCount, 3)];
        SetSlot(program, slots, "iterable", JsValue.FromJsObject(iterable));
        SetSlot(program, slots, "box", JsValue.FromJsObject(box));

        Assert.Equal(1d, ExecuteProgram(program, slots).AsDouble());
        Assert.Equal(1, closeCount);
    }

    [Fact]
    public void Execute_ForOfBreakCloseThrow_StopsBeforeFollowingCode()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function readAfterBreak(iterable, box) {
                for (var value of iterable) {
                    break;
                }

                return box.value;
            }
            """,
            "readAfterBreak");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);

        var getterCount = 0;
        var iterable = CreateSingleValueIterable(
            JsValue.FromDouble(1),
            onReturn: () => throw new ThrowSignal(new JsValue("return boom")));
        var box = new JsObject();
        box.DefineProperty(
            "value",
            new PropertyDescriptor
            {
                Get = new HostFunction(
                    (_, _) =>
                    {
                        getterCount++;
                        return JsValue.FromDouble(0);
                    },
                    isConstructor: false)
            });
        var slots = new JsValue[Math.Max(program.SlotCount, 3)];
        SetSlot(program, slots, "iterable", JsValue.FromJsObject(iterable));
        SetSlot(program, slots, "box", JsValue.FromJsObject(box));

        var (_, context) = ExecuteProgramWithContext(program, slots);

        Assert.True(context.IsThrow);
        Assert.Equal("return boom", context.FlowValue.AsString());
        Assert.Equal(0, getterCount);
    }

    [Fact]
    public void Execute_NestedForOfInnerMoveNextThrow_ClosesOuterIteratorOnly()
    {
        var program = CreateNestedIteratorMoveNextProgram();

        var outerCloseCount = 0;
        var innerCloseCount = 0;
        var outer = CreateSingleValueIterable(
            JsValue.FromDouble(1),
            onReturn: () => outerCloseCount++);
        var inner = CreateThrowingNextIterable(
            "inner next boom",
            onReturn: () => innerCloseCount++);
        var slots = new JsValue[Math.Max(program.SlotCount, 4)];
        SetSlot(program, slots, "outer", JsValue.FromJsObject(outer));
        SetSlot(program, slots, "inner", JsValue.FromJsObject(inner));

        var (_, context) = ExecuteProgramWithContext(program, slots);

        Assert.True(context.IsThrow);
        Assert.Equal("inner next boom", context.FlowValue.AsString());
        Assert.Equal(1, outerCloseCount);
        Assert.Equal(0, innerCloseCount);
    }

    [Fact]
    public void Execute_NestedForOfBodyReturn_ClosesEnteredIteratorsLifoAndPreservesFirstCloseThrow()
    {
        var program = CreateNestedIteratorMoveNextProgram();

        var closeOrder = new List<string>();
        var outer = CreateSingleValueIterable(
            JsValue.FromDouble(1),
            onReturn: () =>
            {
                closeOrder.Add("outer");
                throw new ThrowSignal(new JsValue("outer return boom"));
            });
        var inner = CreateSingleValueIterable(
            JsValue.FromDouble(2),
            onReturn: () =>
            {
                closeOrder.Add("inner");
                throw new ThrowSignal(new JsValue("inner return boom"));
            });
        var slots = new JsValue[Math.Max(program.SlotCount, 4)];
        SetSlot(program, slots, "outer", JsValue.FromJsObject(outer));
        SetSlot(program, slots, "inner", JsValue.FromJsObject(inner));

        var (_, context) = ExecuteProgramWithContext(program, slots);

        Assert.True(context.IsThrow);
        Assert.Equal("inner return boom", context.FlowValue.AsString());
        Assert.Equal(new[] { "inner", "outer" }, closeOrder);
    }

    [Fact]
    public void Execute_NestedForOfInnerBreak_ClosesOnlyInnerBeforeFollowingCode()
    {
        var program = CreateNestedIteratorInnerBreakProgram();

        var outerCloseCount = 0;
        var innerCloseCount = 0;
        var outer = CreateSingleValueIterable(
            JsValue.FromDouble(1),
            onReturn: () => outerCloseCount++);
        var inner = CreateSingleValueIterable(
            JsValue.FromDouble(2),
            onReturn: () => innerCloseCount++);
        var box = new JsObject();
        box.DefineProperty(
            "value",
            new PropertyDescriptor
            {
                Get = new HostFunction(
                    (_, _) => JsValue.FromDouble((outerCloseCount * 10) + innerCloseCount),
                    isConstructor: false)
            });
        var slots = new JsValue[Math.Max(program.SlotCount, 7)];
        SetSlot(program, slots, "outer", JsValue.FromJsObject(outer));
        SetSlot(program, slots, "inner", JsValue.FromJsObject(inner));
        SetSlot(program, slots, "box", JsValue.FromJsObject(box));

        var result = ExecuteProgram(program, slots);

        Assert.Equal(1d, result.AsDouble());
        Assert.Equal(1, outerCloseCount);
        Assert.Equal(1, innerCloseCount);
    }

    [Fact]
    public void Execute_NestedForOfContinueOuter_ClosesInnerIteratorBeforeContinuingOuter()
    {
        var program = CreateNestedIteratorContinueOuterProgram();

        var outerCloseCount = 0;
        var innerCloseCount = 0;
        var outer = CreateCountedIterable(
            count: 2,
            JsValue.FromDouble(1),
            onReturn: () => outerCloseCount++);
        var inner = CreateSingleValueIterable(
            JsValue.FromDouble(2),
            onReturn: () => innerCloseCount++);
        var box = new JsObject();
        box.DefineProperty(
            "value",
            new PropertyDescriptor
            {
                Get = new HostFunction(
                    (_, _) => JsValue.FromDouble((innerCloseCount * 10) + outerCloseCount),
                    isConstructor: false)
            });
        var slots = new JsValue[Math.Max(program.SlotCount, 7)];
        SetSlot(program, slots, "outer", JsValue.FromJsObject(outer));
        SetSlot(program, slots, "inner", JsValue.FromJsObject(inner));
        SetSlot(program, slots, "box", JsValue.FromJsObject(box));

        var result = ExecuteProgram(program, slots);

        Assert.Equal(10d, result.AsDouble());
        Assert.Equal(1, innerCloseCount);
        Assert.Equal(0, outerCloseCount);
    }

    [Fact]
    public void Execute_NestedForOfBreakOuter_ClosesExitedIteratorsInnerToOuter()
    {
        var program = CreateNestedIteratorBreakOuterProgram();

        var closeOrder = new List<string>();
        var outer = CreateSingleValueIterable(
            JsValue.FromDouble(1),
            onReturn: () => closeOrder.Add("outer"));
        var inner = CreateSingleValueIterable(
            JsValue.FromDouble(2),
            onReturn: () => closeOrder.Add("inner"));
        var box = new JsObject();
        box.DefineProperty(
            "value",
            new PropertyDescriptor
            {
                Get = new HostFunction(
                    (_, _) => new JsValue(string.Join(",", closeOrder)),
                    isConstructor: false)
            });
        var slots = new JsValue[Math.Max(program.SlotCount, 7)];
        SetSlot(program, slots, "outer", JsValue.FromJsObject(outer));
        SetSlot(program, slots, "inner", JsValue.FromJsObject(inner));
        SetSlot(program, slots, "box", JsValue.FromJsObject(box));

        var result = ExecuteProgram(program, slots);

        Assert.Equal("inner,outer", result.AsString());
        Assert.Equal(new[] { "inner", "outer" }, closeOrder);
    }

    [Fact]
    public void TryCompile_WhileWithNestedBranchBreak_Compiles()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function breakLoop(n) {
                var total = 0;
                while (n > 0) {
                    if (n > 2) {
                        break;
                    }
                    total = total + n;
                    n = n - 1;
                }

                return total;
            }
            """,
            "breakLoop");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.Break);
    }

    [Fact]
    public void TryCompile_LabeledWhileLoop_Compiles()
    {
        // Labeled breakable regions are now compiled: loop-control targets are compiler-owned
        // (ADR 0253), and the unused label no longer forces a decline.
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function labeledSumTo(n) {
                var total = 0;
                outer: while (n > 0) {
                    total = total + n;
                    n = n - 1;
                }

                return total;
            }
            """,
            "labeledSumTo");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out _, out var reason);
        Assert.True(result, reason);
    }

    [Fact]
    public void TryCompile_LabeledNonLoopStatement_Compiles()
    {
        // Labeled block + labeled break compile to owned Jump bytecode through the resolved-target
        // path; the label no longer forces a decline.
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function labeledBlock(flag) {
                var total = 0;
                blockLabel: {
                    if (flag) {
                        break blockLabel;
                    }
                    total = 1;
                }

                return total;
            }
            """,
            "labeledBlock");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out _, out var reason);
        Assert.True(result, reason);
    }

    [Fact]
    public void TryCompile_ForLoopWithConditionAndPostUpdate_ProducesBackwardJump()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function sumTo(n) {
                var total = 0;
                for (; n > 0; n = n - 1) {
                    total = total + n;
                }

                return total;
            }
            """,
            "sumTo");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(
            program.Instructions.Select((instruction, index) => (instruction, index)),
            pair => pair.instruction.OpCode == UnifiedBytecodeOpCode.Jump && pair.instruction.Operand < pair.index);
    }

    [Fact]
    public void TryCompile_ForLoopWithInitializerAndPostUpdate_ProducesBackwardJump()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function sumTo(n) {
                var total = 0;
                for (var i = 0; i < n; i = i + 1) {
                    total = total + i;
                }

                return total;
            }
            """,
            "sumTo");
        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);
        Assert.Contains(program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(
            program.Instructions.Select((instruction, index) => (instruction, index)),
            pair => pair.instruction.OpCode == UnifiedBytecodeOpCode.Jump && pair.instruction.Operand < pair.index);
    }

    [Fact]
    public void TryCompile_UnsupportedBranchPayload_DeclinesWholeProgram()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function unsupported(a, b, pick) {
                if (pick) {
                    return Math.max(a, b);
                }

                return b;
            }
            """,
            "unsupported");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.False(result);
        Assert.NotEmpty(reason);
        Assert.Empty(program.Instructions);
    }

    [Fact]
    public void TryCompile_AsyncSimpleReturn_ProducesReturnOps()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            async function add(x, y) {
                return x + y;
            }
            """,
            "add");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);
        Assert.Contains(program.Instructions, static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Return);
    }

    [Fact]
    public void TryCompile_GeneratorSimpleReturn_ProducesReturnOps()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function* addViaLocal(a, b) {
                var c = a + b;
                return c;
            }
            """,
            "addViaLocal");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out var program, out var reason);
        Assert.True(result, reason);
        Assert.Contains(program.Instructions, static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Return);
    }

    [Fact(Timeout = 5000)]
    public async Task Execute_ObjectLiteralWithStaticKeyAnonymousFunction_InfersName()
    {
        // AC-3: { greet: function() {} }.greet.name === "greet"
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function makeObj() {
                return { greet: function() {} };
            }
            makeObj().greet.name;
            """);

        Assert.Equal("greet", result);
    }

    [Fact(Timeout = 5000)]
    public async Task Execute_ObjectLiteralWithComputedKeyAnonymousFunction_InfersName()
    {
        // AC-4: { [k]: function() {} } where k === "hello" produces function with .name === "hello"
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function makeObj(k) {
                return { [k]: function() {} };
            }
            makeObj("hello")["hello"].name;
            """);

        Assert.Equal("hello", result);
    }

    private static (ExecutionPlan Plan, bool IsAsync, bool IsGenerator) GetFunctionPlan(string source, string functionName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is FunctionDeclaration f && f.Name?.Name == functionName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return (Assert.IsType<ExecutionPlan>(cache.Plan), declaration.Function.IsAsync, declaration.Function.IsGenerator);
    }

    private JsValue ExecuteProgram(UnifiedBytecodeProgram program, JsValue[] slots)
    {
        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        return UnifiedBytecodeVirtualMachine.Execute(program, slots, context);
    }

    private (JsValue Result, EvaluationContext Context) ExecuteProgramWithContext(
        UnifiedBytecodeProgram program,
        JsValue[] slots)
    {
        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var result = UnifiedBytecodeVirtualMachine.Execute(program, slots, context);
        return (result, context);
    }

    private static UnifiedBytecodeProgram CreateNestedIteratorMoveNextProgram()
    {
        return new UnifiedBytecodeProgram(
            ImmutableArray.Create(
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorInit, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorMoveNext, 1),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 1),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorInit, 2),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorMoveNext, 3),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ReturnUndefined),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ReturnUndefined)),
            MaxStackDepth: 2,
            SlotCount: 6,
            LiteralConstants: ImmutableArray<JsValue>.Empty,
            StringConstants: ImmutableArray<string>.Empty,
            SlotNames: ImmutableArray.Create<string?>("outer", "inner", null, null, null, null),
            ParameterSlotIndices: ImmutableArray.Create(0, 1),
            LexicalSlotIndices: ImmutableArray<int>.Empty,
            CallTargetConstants: ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ScopeDescriptors: ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            TryDescriptors: ImmutableArray<UnifiedBytecodeTryDescriptor>.Empty,
            CatchDescriptors: ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            DriverDescriptors: ImmutableArray.Create(
                new UnifiedBytecodeDriverDescriptor(StateSlot: 2),
                new UnifiedBytecodeDriverDescriptor(
                    StateSlot: 2,
                    ValueSlot: 3,
                    BreakTarget: 7,
                    NextTarget: 3),
                new UnifiedBytecodeDriverDescriptor(StateSlot: 4),
                new UnifiedBytecodeDriverDescriptor(
                    StateSlot: 4,
                    ValueSlot: 5,
                    BreakTarget: 7,
                    NextTarget: 6)));
    }

    private static UnifiedBytecodeProgram CreateNestedIteratorInnerBreakProgram()
    {
        return new UnifiedBytecodeProgram(
            ImmutableArray.Create(
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorInit, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorMoveNext, 1),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 1),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorInit, 2),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorMoveNext, 3),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpWithDriverCleanup, 8),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ReturnUndefined),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 2),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ReturnUndefined)),
            MaxStackDepth: 2,
            SlotCount: 7,
            LiteralConstants: ImmutableArray<JsValue>.Empty,
            StringConstants: ImmutableArray.Create("value"),
            SlotNames: ImmutableArray.Create<string?>("outer", "inner", "box", null, null, null, null),
            ParameterSlotIndices: ImmutableArray.Create(0, 1, 2),
            LexicalSlotIndices: ImmutableArray<int>.Empty,
            CallTargetConstants: ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ScopeDescriptors: ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            TryDescriptors: ImmutableArray<UnifiedBytecodeTryDescriptor>.Empty,
            CatchDescriptors: ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            DriverDescriptors: ImmutableArray.Create(
                new UnifiedBytecodeDriverDescriptor(StateSlot: 3),
                new UnifiedBytecodeDriverDescriptor(
                    StateSlot: 3,
                    ValueSlot: 4,
                    BreakTarget: 11,
                    NextTarget: 3),
                new UnifiedBytecodeDriverDescriptor(StateSlot: 5),
                new UnifiedBytecodeDriverDescriptor(
                    StateSlot: 5,
                    ValueSlot: 6,
                    BreakTarget: 8,
                    NextTarget: 6)));
    }

    private static UnifiedBytecodeProgram CreateNestedIteratorContinueOuterProgram()
    {
        return new UnifiedBytecodeProgram(
            ImmutableArray.Create(
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorInit, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorMoveNext, 1),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 1),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorInit, 2),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorMoveNext, 3),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Continue, 2),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ReturnUndefined),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 2),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 2),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return)),
            MaxStackDepth: 2,
            SlotCount: 7,
            LiteralConstants: ImmutableArray<JsValue>.Empty,
            StringConstants: ImmutableArray.Create("value"),
            SlotNames: ImmutableArray.Create<string?>("outer", "inner", "box", null, null, null, null),
            ParameterSlotIndices: ImmutableArray.Create(0, 1, 2),
            LexicalSlotIndices: ImmutableArray<int>.Empty,
            CallTargetConstants: ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ScopeDescriptors: ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            TryDescriptors: ImmutableArray<UnifiedBytecodeTryDescriptor>.Empty,
            CatchDescriptors: ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            DriverDescriptors: ImmutableArray.Create(
                new UnifiedBytecodeDriverDescriptor(StateSlot: 3),
                new UnifiedBytecodeDriverDescriptor(
                    StateSlot: 3,
                    ValueSlot: 4,
                    BreakTarget: 9,
                    NextTarget: 3,
                    MoveNextTarget: 2),
                new UnifiedBytecodeDriverDescriptor(StateSlot: 5),
                new UnifiedBytecodeDriverDescriptor(
                    StateSlot: 5,
                    ValueSlot: 6,
                    BreakTarget: 8,
                    NextTarget: 6,
                    MoveNextTarget: 5)));
    }

    private static UnifiedBytecodeProgram CreateNestedIteratorBreakOuterProgram()
    {
        return new UnifiedBytecodeProgram(
            ImmutableArray.Create(
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorInit, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorMoveNext, 1),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 1),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorInit, 2),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.IteratorMoveNext, 3),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Break, 9),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ReturnUndefined),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 2),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 2),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return)),
            MaxStackDepth: 2,
            SlotCount: 7,
            LiteralConstants: ImmutableArray<JsValue>.Empty,
            StringConstants: ImmutableArray.Create("value"),
            SlotNames: ImmutableArray.Create<string?>("outer", "inner", "box", null, null, null, null),
            ParameterSlotIndices: ImmutableArray.Create(0, 1, 2),
            LexicalSlotIndices: ImmutableArray<int>.Empty,
            CallTargetConstants: ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ScopeDescriptors: ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            TryDescriptors: ImmutableArray<UnifiedBytecodeTryDescriptor>.Empty,
            CatchDescriptors: ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            DriverDescriptors: ImmutableArray.Create(
                new UnifiedBytecodeDriverDescriptor(StateSlot: 3),
                new UnifiedBytecodeDriverDescriptor(
                    StateSlot: 3,
                    ValueSlot: 4,
                    BreakTarget: 9,
                    NextTarget: 3,
                    MoveNextTarget: 2),
                new UnifiedBytecodeDriverDescriptor(StateSlot: 5),
                new UnifiedBytecodeDriverDescriptor(
                    StateSlot: 5,
                    ValueSlot: 6,
                    BreakTarget: 8,
                    NextTarget: 6,
                    MoveNextTarget: 5)));
    }

    private static UnifiedBytecodeProgram CreateResumableYieldReturnProgram()
    {
        return new UnifiedBytecodeProgram(
            ImmutableArray.Create(
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Yield),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.StoreResumeValue, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, 1),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)BinaryOperator.Add),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return)),
            MaxStackDepth: 2,
            SlotCount: 1,
            LiteralConstants: ImmutableArray.Create(JsValue.FromDouble(10), JsValue.FromDouble(1)),
            StringConstants: ImmutableArray<string>.Empty,
            SlotNames: ImmutableArray.Create<string?>("x"),
            ParameterSlotIndices: ImmutableArray<int>.Empty,
            LexicalSlotIndices: ImmutableArray<int>.Empty,
            CallTargetConstants: ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ScopeDescriptors: ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            TryDescriptors: ImmutableArray<UnifiedBytecodeTryDescriptor>.Empty,
            CatchDescriptors: ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            DriverDescriptors: ImmutableArray<UnifiedBytecodeDriverDescriptor>.Empty);
    }

    private static UnifiedBytecodeProgram CreateAbruptThroughFinallyProgram(UnifiedBytecodeOpCode abruptOpCode)
    {
        return new UnifiedBytecodeProgram(
            ImmutableArray.Create(
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.EnterTry, 0),
                new UnifiedBytecodeInstruction(abruptOpCode, 6),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ReturnUndefined),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.StoreSlot, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.EndFinally, 6),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, 0),
                new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return)),
            MaxStackDepth: 1,
            SlotCount: 1,
            LiteralConstants: ImmutableArray.Create(JsValue.FromDouble(11)),
            StringConstants: ImmutableArray<string>.Empty,
            SlotNames: ImmutableArray.Create<string?>("value"),
            ParameterSlotIndices: ImmutableArray<int>.Empty,
            LexicalSlotIndices: ImmutableArray<int>.Empty,
            CallTargetConstants: ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ScopeDescriptors: ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            TryDescriptors: ImmutableArray.Create(new UnifiedBytecodeTryDescriptor(
                HandlerTarget: -1,
                FinallyTarget: 3,
                EndFinallyTarget: 5,
                LeaveTryTarget: -1)),
            CatchDescriptors: ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            DriverDescriptors: ImmutableArray<UnifiedBytecodeDriverDescriptor>.Empty);
    }

    private static JsObject CreateSingleValueIterable(JsValue value, Action onReturn)
    {
        var iterable = new JsObject();
        var iterator = new JsObject();
        var moved = false;
        iterator.SetHostedProperty(
            "next",
            (_, _) =>
            {
                if (moved)
                {
                    return JsValue.FromJsObject(CreateIteratorResult(JsValue.Undefined, done: true));
                }

                moved = true;
                return JsValue.FromJsObject(CreateIteratorResult(value, done: false));
            });
        iterator.SetHostedProperty(
            "return",
            (_, _) =>
            {
                onReturn();
                return JsValue.FromJsObject(new JsObject());
            });
        iterable.SetHostedProperty(SymbolKeys.Iterator, (_, _) => JsValue.FromJsObject(iterator));
        return iterable;
    }

    private static JsObject CreateCountedIterable(int count, JsValue value, Action onReturn)
    {
        var iterable = new JsObject();
        var iterator = new JsObject();
        var moved = 0;
        iterator.SetHostedProperty(
            "next",
            (_, _) =>
            {
                if (moved >= count)
                {
                    return JsValue.FromJsObject(CreateIteratorResult(JsValue.Undefined, done: true));
                }

                moved++;
                return JsValue.FromJsObject(CreateIteratorResult(value, done: false));
            });
        iterator.SetHostedProperty(
            "return",
            (_, _) =>
            {
                onReturn();
                return JsValue.FromJsObject(new JsObject());
            });
        iterable.SetHostedProperty(SymbolKeys.Iterator, (_, _) => JsValue.FromJsObject(iterator));
        return iterable;
    }

    private static JsObject CreateSendRecordingIterable(JsValue firstValue, List<JsValue> received)
    {
        var iterable = new JsObject();
        var iterator = new JsObject();
        var moved = false;
        iterator.SetHostedProperty(
            "next",
            (_, args) =>
            {
                var sent = args.Count == 0 ? JsValue.Undefined : args[0];
                received.Add(sent);
                if (moved)
                {
                    return JsValue.FromJsObject(CreateIteratorResult(sent, done: true));
                }

                moved = true;
                return JsValue.FromJsObject(CreateIteratorResult(firstValue, done: false));
            });
        iterable.SetHostedProperty(SymbolKeys.Iterator, (_, _) => JsValue.FromJsObject(iterator));
        return iterable;
    }

    private static JsObject CreateThrowingNextIterable(string message, Action onReturn)
    {
        var iterable = new JsObject();
        var iterator = new JsObject();
        iterator.SetHostedProperty("next", (_, _) => throw new ThrowSignal(new JsValue(message)));
        iterator.SetHostedProperty(
            "return",
            (_, _) =>
            {
                onReturn();
                return JsValue.FromJsObject(new JsObject());
            });
        iterable.SetHostedProperty(SymbolKeys.Iterator, (_, _) => JsValue.FromJsObject(iterator));
        return iterable;
    }

    private static JsObject CreateIteratorResult(JsValue value, bool done)
    {
        var result = new JsObject();
        result.SetProperty("value", value);
        result.SetProperty("done", done ? JsValue.True : JsValue.False);
        return result;
    }

    private static void SetSlot(UnifiedBytecodeProgram program, JsValue[] slots, string name, JsValue value)
    {
        var slotIndex = program.SlotNames.IndexOf(name);
        Assert.True(slotIndex >= 0, name);
        slots[slotIndex] = value;
    }

    public static TheoryData<string, string, bool> NumericParityOperators =>
        new()
        {
            { "add", "+", false },
            { "subtract", "-", false },
            { "multiply", "*", false },
            { "divide", "/", false },
            { "modulo", "%", false },
            { "lessThan", "<", true },
            { "lessThanOrEqual", "<=", true },
            { "greaterThan", ">", true },
            { "greaterThanOrEqual", ">=", true }
        };
}
