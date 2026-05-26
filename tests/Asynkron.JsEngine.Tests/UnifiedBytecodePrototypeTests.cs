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
        var plan = GetFunctionPlan("""
            function add(x, y) {
                return x + y;
            }
            """,
            "add");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, out var program, out var reason);

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
        var plan = GetFunctionPlan("""
            function add(x, y) {
                return x + y;
            }
            """,
            "add");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, out var program, out var reason);
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
        var plan = GetFunctionPlan("""
            function addViaLocal(a, b) {
                var c = a + b;
                return c;
            }
            """,
            "addViaLocal");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, out var program, out var reason);

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
    public void TryCompile_NonLinearPlan_Declines()
    {
        var plan = GetFunctionPlan("""
            function addOrSub(a, b, pickAdd) {
                if (pickAdd) {
                    return a + b;
                }

                return a - b;
            }
            """,
            "addOrSub");

        var result = UnifiedBytecodeCompiler.TryCompile(plan, out _, out var reason);
        Assert.False(result);
        Assert.NotEmpty(reason);
    }

    private static ExecutionPlan GetFunctionPlan(string source, string functionName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is FunctionDeclaration f && f.Name?.Name == functionName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}
