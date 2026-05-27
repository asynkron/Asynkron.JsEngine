using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.Debugging)]
public sealed class UnifiedBytecodeProductionEligibilityTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public void Evaluate_LinearSlotLiteralReturnPlan_Accepts()
    {
        var plan = GetFunctionPlan("""
            function passThrough(x) {
                var y = x;
                return y;
            }
            """,
            "passThrough");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.All(
            result.Program.Instructions,
            instruction => Assert.Contains(
                instruction.OpCode,
                new[]
                {
                    UnifiedBytecodeOpCode.LoadSlot,
                    UnifiedBytecodeOpCode.LoadLiteral,
                    UnifiedBytecodeOpCode.StoreSlot,
                    UnifiedBytecodeOpCode.Return
                }));
    }

    [Fact]
    public void Evaluate_AsyncLikeActivation_DeclinesBeforePlanInspection()
    {
        var plan = GetFunctionPlan("""
            function passThrough(x) {
                return x;
            }
            """,
            "passThrough");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.AsyncLikeFunction, result.Code);
        Assert.Contains("Async-like", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_GeneratorActivation_DeclinesBeforePlanInspection()
    {
        var plan = GetFunctionPlan("""
            function passThrough(x) {
                return x;
            }
            """,
            "passThrough");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.GeneratorFunction, result.Code);
        Assert.Contains("Generator", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, false, false, (int)UnifiedBytecodeProductionDeclineCode.CapturedOrDynamicActivation)]
    [InlineData(false, true, false, false, (int)UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency)]
    [InlineData(false, false, true, false, (int)UnifiedBytecodeProductionDeclineCode.ThisDependency)]
    [InlineData(false, false, false, true, (int)UnifiedBytecodeProductionDeclineCode.NewTargetDependency)]
    public void Evaluate_ActivationDependencies_DeclineBeforeCompile(
        bool capturedOrDynamic,
        bool argumentsDependency,
        bool thisDependency,
        bool newTargetDependency,
        int expectedCode)
    {
        var plan = GetFunctionPlan("""
            function passThrough(x) {
                return x;
            }
            """,
            "passThrough");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                HasCapturedOrDynamicActivation: capturedOrDynamic,
                HasArgumentsObjectDependency: argumentsDependency,
                HasThisDependency: thisDependency,
                HasNewTargetDependency: newTargetDependency));

        Assert.False(result.IsEligible);
        Assert.Equal((UnifiedBytecodeProductionDeclineCode)expectedCode, result.Code);
    }

    [Fact]
    public void Evaluate_CallExpressionPlan_DeclinesWithCallDependency()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, x) {
                return fn(x);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.CallDependency, result.Code);
        Assert.Contains("Call/construct", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ArgumentsAccess_DeclinesWithArgumentsDependency()
    {
        var plan = GetFunctionPlan("""
            function readArguments() {
                return arguments[0];
            }
            """,
            "readArguments");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency, result.Code);
    }

    [Fact]
    public void Evaluate_DynamicIdentifierLookup_DeclinesWithDynamicLookupDependency()
    {
        var plan = GetFunctionPlan("""
            function readExternal() {
                return externalValue;
            }
            """,
            "readExternal");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency, result.Code);
        Assert.Contains("dynamic lookup", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_LabeledLoop_DeclinesWithLabelControlFlow()
    {
        var plan = GetFunctionPlan("""
            function labeled(n) {
                outer: while (n > 0) {
                    n = n - 1;
                }

                return n;
            }
            """,
            "labeled");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.LabelControlFlow, result.Code);
    }

    [Fact]
    public void Evaluate_BreakControlFlow_DeclinesWithBreakContinueCode()
    {
        var plan = GetFunctionPlan("""
            function breakLoop(n) {
                while (n > 0) {
                    break;
                }

                return n;
            }
            """,
            "breakLoop");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.BreakOrContinueControlFlow, result.Code);
    }

    [Fact]
    public void Evaluate_ContinueControlFlow_DeclinesWithBreakContinueCode()
    {
        var plan = GetFunctionPlan("""
            function continueLoop(n) {
                while (n > 0) {
                    n = n - 1;
                    continue;
                }

                return n;
            }
            """,
            "continueLoop");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.BreakOrContinueControlFlow, result.Code);
    }

    [Fact]
    public void Evaluate_BinaryOpcodePlan_Accepts()
    {
        var plan = GetFunctionPlan("""
            function add(a, b) {
                return a + b;
            }
            """,
            "add");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: (int)BinaryOperator.Add });
    }

    [Fact]
    public void Evaluate_StringConcatenationBinary_Accepts()
    {
        var plan = GetFunctionPlan("""
            function concatWithSuffix(value) {
                return value + "!";
            }
            """,
            "concatWithSuffix");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: (int)BinaryOperator.Add });
    }

    [Fact]
    public void Evaluate_CoercingComparisonBinary_Accepts()
    {
        var plan = GetFunctionPlan("""
            function compareUnknown(a, b) {
                return a < b;
            }
            """,
            "compareUnknown");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: (int)BinaryOperator.LessThan });
    }

    [Theory]
    [MemberData(nameof(CompiledBinaryOperators))]
    public void Evaluate_CompiledBinaryOperator_AcceptsProductionSubset(
        string functionName,
        string expression,
        int expectedOperator)
    {
        var plan = GetFunctionPlan($$"""
            function {{functionName}}(a, b) {
                return a {{expression}} b;
            }
            """,
            functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: var operand } &&
            operand == expectedOperator);
    }

    [Fact]
    public void Evaluate_UnsupportedBinaryOperator_DeclinesWithOperatorSpecificReason()
    {
        var plan = GetFunctionPlan("""
            function equal(a, b) {
                return a == b;
            }
            """,
            "equal");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.PrototypeOnlyBinaryOpcode, result.Code);
        Assert.Contains("operator 'Equal'", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_BranchPlan_AcceptsJumpIfFalseAndJoinJump()
    {
        var plan = GetFunctionPlan("""
            function pick(flag) {
                var result = 1;
                if (flag) {
                    result = 2;
                }

                return result;
            }
            """,
            "pick");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.Jump);
    }

    [Fact]
    public void Evaluate_DirectBranchReturnPlan_Accepts()
    {
        var plan = GetFunctionPlan("""
            function pick(flag) {
                if (flag) {
                    return 1;
                }

                return 2;
            }
            """,
            "pick");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_CanonicalWhilePlan_AcceptsBackwardJump()
    {
        var plan = GetFunctionPlan("""
            function clear(flag) {
                while (flag) {
                    flag = false;
                }

                return flag;
            }
            """,
            "clear");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(
            result.Program.Instructions.Select((instruction, index) => (instruction, index)),
            pair => pair.instruction.OpCode == UnifiedBytecodeOpCode.Jump && pair.instruction.Operand < pair.index);
    }

    [Fact]
    public void Evaluate_BranchWithBinaryCondition_Accepts()
    {
        var plan = GetFunctionPlan("""
            function pickLess(a, b) {
                if (a < b) {
                    return a;
                }

                return b;
            }
            """,
            "pickLess");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: (int)BinaryOperator.LessThan });
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
    }

    [Fact]
    public void Evaluate_WhileWithBinaryCondition_Accepts()
    {
        var plan = GetFunctionPlan("""
            function lowerToLimit(n, limit) {
                while (n >= limit) {
                    n = n - 1;
                }

                return n;
            }
            """,
            "lowerToLimit");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: (int)BinaryOperator.GreaterThanOrEqual });
        Assert.Contains(
            result.Program.Instructions.Select((instruction, index) => (instruction, index)),
            pair => pair.instruction.OpCode == UnifiedBytecodeOpCode.Jump && pair.instruction.Operand < pair.index);
    }

    public static TheoryData<string, string, int> CompiledBinaryOperators =>
        new()
        {
            { "add", "+", (int)BinaryOperator.Add },
            { "subtract", "-", (int)BinaryOperator.Subtract },
            { "multiply", "*", (int)BinaryOperator.Multiply },
            { "divide", "/", (int)BinaryOperator.Divide },
            { "modulo", "%", (int)BinaryOperator.Modulo },
            { "lessThan", "<", (int)BinaryOperator.LessThan },
            { "lessThanOrEqual", "<=", (int)BinaryOperator.LessThanOrEqual },
            { "greaterThan", ">", (int)BinaryOperator.GreaterThan },
            { "greaterThanOrEqual", ">=", (int)BinaryOperator.GreaterThanOrEqual }
        };

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
