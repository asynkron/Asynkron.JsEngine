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
    public void TryCompile_ComputedPropertyReadOutsideBoundary_Declines()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function read(box, left, right) {
                return box[left + right];
            }
            """,
            "read");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out _, out var reason);

        Assert.False(result);
        Assert.Contains("RequireObjectCoercible", reason, StringComparison.Ordinal);
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
        Assert.Equal(UnifiedBytecodeOpCode.StoreSlot, program.Instructions[3].OpCode);
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
    public void TryCompile_WhileWithNestedBranchBreak_DeclinesWithLoopReason()
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

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out _, out var reason);
        Assert.False(result);
        Assert.Contains("loop", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCompile_LabeledWhileLoop_DeclinesWithLabelReason()
    {
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
        Assert.False(result);
        Assert.Contains("labels", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCompile_LabeledNonLoopStatement_DeclinesWithLabelReason()
    {
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
        Assert.False(result);
        Assert.Contains("labels", reason, StringComparison.OrdinalIgnoreCase);
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
    public void TryCompile_AsyncSimpleReturn_Declines()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            async function add(x, y) {
                return x + y;
            }
            """,
            "add");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out _, out var reason);
        Assert.False(result);
        Assert.Contains("not eligible", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCompile_GeneratorSimpleReturn_Declines()
    {
        var (plan, isAsync, isGenerator) = GetFunctionPlan("""
            function* addViaLocal(a, b) {
                var c = a + b;
                return c;
            }
            """,
            "addViaLocal");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out _, out var reason);
        Assert.False(result);
        Assert.Contains("not eligible", reason, StringComparison.Ordinal);
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
