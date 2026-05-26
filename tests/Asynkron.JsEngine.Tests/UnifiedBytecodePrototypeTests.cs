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

        var slotCount = Math.Max(plan.ActivationSlots!.SlotCount, 2);
        var slots = new JsValue[slotCount];
        slots[program.Instructions[0].Operand] = JsValue.FromDouble(2);
        slots[program.Instructions[1].Operand] = JsValue.FromDouble(3);

        var value = UnifiedBytecodeVirtualMachine.Execute(program, slots);
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

        var slots = new JsValue[Math.Max(plan.ActivationSlots!.SlotCount, 3)];
        slots[program.Instructions[0].Operand] = JsValue.FromDouble(2);
        slots[program.Instructions[1].Operand] = JsValue.FromDouble(3);
        var value = UnifiedBytecodeVirtualMachine.Execute(program, slots);
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

        var slots = new JsValue[Math.Max(plan.ActivationSlots!.SlotCount, 2)];
        slots[program.Instructions[0].Operand] = JsValue.FromDouble(2);
        slots[program.Instructions[1].Operand] = JsValue.FromDouble(3);

        var value = UnifiedBytecodeVirtualMachine.Execute(program, slots);
        Assert.Equal(expected, value.AsDouble(), 12);
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

        var slots = new JsValue[Math.Max(plan.ActivationSlots!.SlotCount, 2)];
        SetSlot(plan, slots, "a", JsValue.FromDouble(2));
        SetSlot(plan, slots, "b", JsValue.FromDouble(3));
        Assert.Equal(2d, UnifiedBytecodeVirtualMachine.Execute(program, slots).AsDouble());

        SetSlot(plan, slots, "a", JsValue.FromDouble(5));
        SetSlot(plan, slots, "b", JsValue.FromDouble(3));
        Assert.Equal(3d, UnifiedBytecodeVirtualMachine.Execute(program, slots).AsDouble());
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

        var slots = new JsValue[Math.Max(plan.ActivationSlots!.SlotCount, 4)];
        SetSlot(plan, slots, "a", JsValue.FromDouble(2));
        SetSlot(plan, slots, "b", JsValue.FromDouble(3));
        SetSlot(plan, slots, "pick", JsValue.True);
        Assert.Equal(10d, UnifiedBytecodeVirtualMachine.Execute(program, slots).AsDouble());

        SetSlot(plan, slots, "a", JsValue.FromDouble(2));
        SetSlot(plan, slots, "b", JsValue.FromDouble(3));
        SetSlot(plan, slots, "pick", JsValue.False);
        Assert.Equal(4d, UnifiedBytecodeVirtualMachine.Execute(program, slots).AsDouble());
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

        var slots = new JsValue[Math.Max(plan.ActivationSlots!.SlotCount, 2)];
        SetSlot(plan, slots, "n", JsValue.FromDouble(n));
        Assert.Equal(expected, UnifiedBytecodeVirtualMachine.Execute(program, slots).AsDouble());
    }

    [Fact]
    public void TryCompile_WhileWithBreak_DeclinesWithExplicitReason()
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
        Assert.Contains("Unsupported", reason, StringComparison.Ordinal);
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
    public void TryCompile_ForLoopWithConditionAndPostUpdate_DeclinesWithExplicitReason()
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

        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out _, out var reason);
        Assert.False(result);
        Assert.Contains("loop", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCompile_ForLoopWithInitializerAndPostUpdate_DeclinesWithExplicitReason()
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
        var result = UnifiedBytecodeCompiler.TryCompile(plan, isAsync, isGenerator, out _, out var reason);
        Assert.False(result);
        Assert.Contains("loop", reason, StringComparison.OrdinalIgnoreCase);
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

    private static void SetSlot(ExecutionPlan plan, JsValue[] slots, string name, JsValue value)
    {
        Assert.True(plan.ActivationSlots!.SlotMap.TryGetValue(Symbol.Intern(name), out var slotIndex), name);
        slots[slotIndex] = value;
    }
}
