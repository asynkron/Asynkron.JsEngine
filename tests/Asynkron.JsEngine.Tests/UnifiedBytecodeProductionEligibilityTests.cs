using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
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
                    UnifiedBytecodeOpCode.InitializeSlot,
                    UnifiedBytecodeOpCode.Return
                }));
    }

    [Fact]
    public void Evaluate_UnresolvedIncrementSlotCompilerFailure_ReportsUpdateTargetReason()
    {
        var plan = GetFunctionPlan("""
            function update() {
                var x = 0;
                x++;
                return 1;
            }
            """,
            "update");

        var increment = Assert.Single(plan.Instructions.OfType<IncrementSlotInstruction>());
        var unresolvedIncrement = increment with { ScopeId = -1, SlotIndex = -1, FlatSlotId = -1 };
        var activationSlots = plan.ActivationSlots ?? throw new InvalidOperationException("Expected activation slots.");
        var malformedActivationSlots = activationSlots with
        {
            SlotMap = activationSlots.SlotMap.SetItem(
                increment.TargetSymbol,
                activationSlots.SlotCount + 10)
        };
        var malformedPlan = plan with
        {
            Instructions = plan.Instructions.Replace(increment, unresolvedIncrement),
            ActivationSlots = malformedActivationSlots
        };

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            malformedPlan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("Unsupported update target 'x'.", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IncrementSlotInstruction), result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_SyncUsingDeclaration_AcceptsWithDisposableRegistration()
    {
        var plan = GetFunctionPlan("""
            function disposeLater(resource) {
                using value = resource;
                return 1;
            }
            """,
            "disposeLater");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.RegisterDisposable);
    }

    [Fact]
    public void Evaluate_AwaitUsingDeclaration_StaysDeclined()
    {
        var plan = GetFunctionPlan("""
            async function disposeAsyncLater(resource) {
                await using value = resource;
                return 1;
            }
            """,
            "disposeAsyncLater");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("await using", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_LinearSlotLiteralReturnPlan_ReturnsSlotValueInProductionVm()
    {
        var plan = GetFunctionPlan("""
            function passThrough(x) {
                var y = x;
                return y;
            }
            """,
            "passThrough");

        var eligibility = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());
        Assert.True(eligibility.IsEligible, eligibility.Reason);

        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var slots = new JsValue[Math.Max(eligibility.Program.SlotCount, 1)];
        SetSlot(eligibility.Program, slots, "x", JsValue.FromDouble(42));

        var result = UnifiedBytecodeVirtualMachine.Execute(eligibility.Program, slots, context);

        Assert.Equal(42d, result.AsDouble());
    }

    [Fact]
    public void EvaluateScript_TopLevelPropertyAccessLoop_AcceptsWithScriptCompletionSlot()
    {
        var plan = GetScriptPlan("""
            let obj = {
                a: { b: { c: { d: { e: 1 } } } },
                x: 10,
                y: 20,
                z: 30
            };
            let sum = 0;
            for (let i = 0; i < 5; i++) {
                sum += obj.a.b.c.d.e;
                sum += obj.x + obj.y + obj.z;
            }
            sum;
            """);

        var result = UnifiedBytecodeProductionEligibility.EvaluateScript(plan);

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.True(result.Program.ScriptCompletionSlot >= 0);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.StoreSlot &&
            instruction.Operand == result.Program.ScriptCompletionSlot);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadSlot &&
            instruction.Operand == result.Program.ScriptCompletionSlot);
        Assert.Equal(
            new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return),
            result.Program.Instructions[^1]);
    }

    [Fact]
    public void EvaluateScript_TopLevelSimpleArithmeticBuiltins_AcceptsDynamicGlobalMemberCalls()
    {
        var plan = GetScriptPlan("""
            let x = 1 + 2 * 3 - 4 / 2;
            let y = x * x + Math.sqrt(16);
            let z = y % 7 + Math.pow(2, 10);
            z;
            """);

        var result = UnifiedBytecodeProductionEligibility.EvaluateScript(plan);

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.True(result.Program.ScriptCompletionSlot >= 0);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeclareDynamicLexical);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.InitializeDynamicLexical);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void EvaluateScript_TopLevelDeclarationOnly_AcceptsWithScriptCompletionSlot()
    {
        var plan = GetScriptPlan("""
            let x = 1;
            """);

        var result = UnifiedBytecodeProductionEligibility.EvaluateScript(plan);

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.True(result.Program.ScriptCompletionSlot >= 0);
        Assert.Equal(
            new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return),
            result.Program.Instructions[^1]);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadSlot &&
            instruction.Operand == result.Program.ScriptCompletionSlot);
    }

    [Fact]
    public void EvaluateScript_BlockScopedTypeOfAfterForLet_AcceptsDynamicTypeOf()
    {
        var plan = GetScriptPlan("""
            for (let i = 0; i < 1; i++) {
            }

            typeof i;
            """);

        var result = UnifiedBytecodeProductionEligibility.EvaluateScript(plan);

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.TypeOfDynamicIdentifier);
        Assert.DoesNotContain(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.TypeOfIdentifier);
    }

    [Fact]
    public void EvaluateScript_BlockScopedTypeOfCallArgument_AcceptsDynamicTypeOfOperand()
    {
        var plan = GetScriptPlan("""
            function id(value) {
                return value;
            }

            for (let i = 0; i < 1; i++) {
            }

            id(typeof i);
            """);

        var result = UnifiedBytecodeProductionEligibility.EvaluateScript(plan);

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.TypeOfDynamicIdentifier);
        Assert.DoesNotContain(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.TypeOfIdentifier);
    }

    [Fact]
    public void EvaluateScript_TopLevelObjectVarDestructuring_AcceptsWithDynamicTargets()
    {
        var plan = GetScriptPlan("""
            var o = { a: 1, b: 2 };
            var { a, b } = o;
            a + b;
            """);

        var result = UnifiedBytecodeProductionEligibility.EvaluateScript(plan);

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ObjectDestructuringInit);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ObjectDestructuringProperty);
        // The destructured var targets must store dynamically (no flat slot at script scope),
        // so the property driver descriptors carry a dynamic target name index.
        Assert.Contains(result.Program.DriverDescriptors, descriptor =>
            descriptor.TargetSlot < 0 && descriptor.TargetNameConstantIndex >= 0);
    }

    [Fact]
    public void EvaluateScript_TopLevelArrayVarDestructuring_AcceptsWithDynamicTargets()
    {
        var plan = GetScriptPlan("""
            var arr = [1, 2];
            var [ x, y ] = arr;
            x + y;
            """);

        var result = UnifiedBytecodeProductionEligibility.EvaluateScript(plan);

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringInit);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringElement);
        Assert.Contains(result.Program.DriverDescriptors, descriptor =>
            descriptor.TargetSlot < 0 && descriptor.TargetNameConstantIndex >= 0);
    }

    [Fact]
    public void EvaluateScript_TopLevelArrayVarRestDestructuring_AcceptsWithDynamicTargets()
    {
        var plan = GetScriptPlan("""
            var arr = [1, 2, 3];
            var [ head, ...tail ] = arr;
            head + tail.length;
            """);

        var result = UnifiedBytecodeProductionEligibility.EvaluateScript(plan);

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringRest);
    }

    [Fact]
    public void EvaluateScript_TopLevelConstDestructuring_AcceptsWithDynamicLexicalTargets()
    {
        var plan = GetScriptPlan("""
            const o = { a: 1, b: 2 };
            const { a, b } = o;
            a + b;
            """);

        var result = UnifiedBytecodeProductionEligibility.EvaluateScript(plan);

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ObjectDestructuringProperty);
        Assert.Contains(result.Program.DriverDescriptors, descriptor =>
            descriptor.TargetSlot < 0 &&
            descriptor.TargetNameConstantIndex >= 0 &&
            descriptor.TargetVariableKind == VariableKind.Const);
    }

    [Fact]
    public void EvaluateScript_TopLevelLetArrayDestructuring_AcceptsWithDynamicLexicalTargets()
    {
        var plan = GetScriptPlan("""
            let values = [1, 2, 3];
            let [head, ...tail] = values;
            head + tail.length;
            """);

        var result = UnifiedBytecodeProductionEligibility.EvaluateScript(plan);

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringRest);
        Assert.Contains(result.Program.DriverDescriptors, descriptor =>
            descriptor.TargetSlot < 0 &&
            descriptor.TargetNameConstantIndex >= 0 &&
            descriptor.TargetVariableKind == VariableKind.Let);
    }

    public static TheoryData<string, string, int[]> C3RepresentativeScriptRows => new()
    {
        {
            "property-read-call-control-flow",
            """
            let box = { value: 1, add(n) { return this.value + n; } };
            let total = 0;
            for (let i = 0; i < 3; i++) {
                if (i === 1) {
                    continue;
                }

                total += box.add(i);
            }

            total;
            """,
            [
                (int)UnifiedBytecodeOpCode.PrepareNamedCallTarget,
                (int)UnifiedBytecodeOpCode.JumpIfFalse,
                (int)UnifiedBytecodeOpCode.Jump
            ]
        },
        {
            "script-var-destructuring-dynamic-targets",
            """
            var source = { head: 2, tail: 3, extra: 5 };
            var { head, ...rest } = source;
            head + rest.tail + rest.extra;
            """,
            [
                (int)UnifiedBytecodeOpCode.ObjectDestructuringInit,
                (int)UnifiedBytecodeOpCode.ObjectDestructuringRest
            ]
        },
        {
            "dynamic-global-read-call-and-completion",
            """
            let base = Math.max(1, 5);
            let next = Math.pow(base, 2);
            next - Math.sqrt(16);
            """,
            [
                (int)UnifiedBytecodeOpCode.LoadDynamicIdentifier,
                (int)UnifiedBytecodeOpCode.PrepareNamedCallTarget,
                (int)UnifiedBytecodeOpCode.CallInvocationBoundary
            ]
        }
    };

    [Theory]
    [MemberData(nameof(C3RepresentativeScriptRows))]
    public void EvaluateScript_C3RepresentativeAdmittedShapes_InheritSharedProductionGate(
        string name,
        string source,
        int[] expectedOpCodes)
    {
        var plan = GetScriptPlan(source);

        var result = UnifiedBytecodeProductionEligibility.EvaluateScript(plan);

        Assert.True(result.IsEligible, $"{name}: {result.Code} {result.Reason}");
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.True(result.Program.ScriptCompletionSlot >= 0);
        foreach (var expectedOpCode in expectedOpCodes)
        {
            Assert.Contains(result.Program.Instructions, instruction =>
                instruction.OpCode == (UnifiedBytecodeOpCode)expectedOpCode);
        }
    }

    [Fact]
    public void EvaluateScript_C3TrueDynamicResidue_DirectEvalInjectedBindingStaysDeclined()
    {
        var plan = GetScriptPlan("""
            eval("var injected = 1");
            injected;
            """);

        var result = UnifiedBytecodeProductionEligibility.EvaluateScript(plan);

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.CallDependency, result.Code);
        Assert.Contains("eval", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("var injected = 1;")]
    [InlineData("let injected = 1;")]
    [InlineData("const injected = 1;")]
    [InlineData("function injected() { return 1; }")]
    [InlineData("class Injected {}")]
    public void Evaluate_DirectEvalDeclarationLiteral_StaysDeclinedBeforeProductionVm(string evalSource)
    {
        var plan = GetFunctionPlan(
            $$"""
            function invokeEval() {
                eval({{ToJavaScriptStringLiteral(evalSource)}});
                return 1;
            }
            """,
            "invokeEval");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.CallDependency, result.Code);
        Assert.Contains("eval", result.Reason, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void EvaluateResumable_SimpleGeneratorYieldSend_Accepts()
    {
        var plan = GetFunctionPlan("""
            function* gen(input) {
                var x = yield input;
                return x + 1;
            }
            """,
            "gen");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Yield);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.StoreResumeValue);
    }

    [Fact]
    public void EvaluateResumable_YieldStar_AcceptsAfterDelegatedAbruptResumeIsModeled()
    {
        var plan = GetFunctionPlan("""
            function* gen(values) {
                yield* values;
            }
            """,
            "gen");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.YieldStar);
    }

    [Fact]
    public void EvaluateResumable_GeneratorBreakAfterYield_AcceptsBreakInstruction()
    {
        var plan = GetFunctionPlan("""
            function* gen(values) {
                for (var value of values) {
                    yield value;
                    break;
                }

                return "done";
            }
            """,
            "gen");
        Assert.Contains(plan.Instructions, static instruction => instruction is BreakInstruction);

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Break);
    }

    [Fact]
    public void EvaluateResumable_GeneratorContinueAfterYield_AcceptsContinueInstruction()
    {
        var plan = GetFunctionPlan("""
            function* gen(values) {
                for (var value of values) {
                    yield value;
                    continue;
                }
            }
            """,
            "gen");
        Assert.Contains(plan.Instructions, static instruction => instruction is ContinueInstruction);

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Continue);
    }

    [Fact]
    public void EvaluateResumable_AsyncBreakAndContinueAfterAwait_AcceptsControlInstructions()
    {
        var breakPlan = GetFunctionPlan("""
            async function breakAfterAwait(gate) {
                for (var index = 0; index < 2; index = index + 1) {
                    await gate;
                    break;
                }

                return 1;
            }
            """,
            "breakAfterAwait");
        Assert.Contains(breakPlan.Instructions, static instruction => instruction is BreakInstruction);

        var breakResult = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            breakPlan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(breakResult.IsEligible, breakResult.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, breakResult.Code);
        Assert.Contains(
            breakResult.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Break);

        var continuePlan = GetFunctionPlan("""
            async function continueAfterAwait(gate) {
                for (var index = 0; index < 2; index = index + 1) {
                    await gate;
                    continue;
                }

                return 1;
            }
            """,
            "continueAfterAwait");
        Assert.Contains(continuePlan.Instructions, static instruction => instruction is ContinueInstruction);

        var continueResult = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            continuePlan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(continueResult.IsEligible, continueResult.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, continueResult.Code);
        Assert.Contains(
            continueResult.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Continue);
    }

    [Fact]
    public void EvaluateResumable_NestedTryFinallyAroundYield_DeclinesPendingCleanupChain()
    {
        var plan = GetFunctionPlan("""
            function* gen() {
                try {
                    try {
                        yield 1;
                    } finally {
                    }
                } finally {
                }
            }
            """,
            "gen");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("nested try/finally cleanup chain", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("&&", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitFalse)]
    [InlineData("||", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitTrue)]
    [InlineData("??", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish)]
    public void EvaluateResumable_ShortCircuitReturnExpression_AcceptsImplementedVmOpcodes(
        string operatorText,
        int expectedOpcode)
    {
        var plan = GetFunctionPlan($$"""
            function* gen(left, right) {
                return left {{operatorText}} right;
            }
            """,
            "gen");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == (UnifiedBytecodeOpCode)expectedOpcode);
    }

    [Theory]
    [InlineData("&&", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitFalse)]
    [InlineData("||", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitTrue)]
    [InlineData("??", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish)]
    public void EvaluateResumable_ParenthesizedShortCircuitYieldExpression_AcceptsImplementedVmOpcodes(
        string operatorText,
        int expectedOpcode)
    {
        var plan = GetFunctionPlan($$"""
            function* gen(left, right) {
                yield (left {{operatorText}} right);
            }
            """,
            "gen");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == (UnifiedBytecodeOpCode)expectedOpcode);
    }

    [Fact]
    public void EvaluateResumable_ReturnYield_AcceptsSyntheticResumeSlot()
    {
        var plan = GetFunctionPlan("""
            function* gen(value) {
                return yield value;
            }
            """,
            "gen");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.StoreResumeValue);
    }

    [Fact]
    public void EvaluateResumable_AsyncLikeGeneratorActivation_SimpleYieldAccepts()
    {
        var plan = GetFunctionPlan("""
            async function* gen() {
                yield 1;
            }
            """,
            "gen");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true, IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Yield);
    }

    [Fact]
    public void EvaluateResumable_AsyncGeneratorYieldStar_AdmitsYieldStarOpcode()
    {
        var plan = GetFunctionPlan("""
            async function* gen(values) {
                yield* values;
            }
            """,
            "gen");
        Assert.Contains(plan.Instructions, static instruction => instruction is YieldStarInstruction);

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true, IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.YieldStar);
    }

    [Fact]
    public void EvaluateResumable_AsyncGeneratorAwaitedYieldStar_AdmitsAwaitValueAndYieldStarOpcode()
    {
        var plan = GetFunctionPlan("""
            async function* gen(values) {
                yield* await values;
            }
            """,
            "gen");
        Assert.Contains(plan.Instructions, static instruction => instruction is YieldStarInstruction);

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true, IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.AwaitValue);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.YieldStar);
    }

    [Theory]
    [InlineData(true, false, false, (int)UnifiedBytecodeProductionDeclineCode.CapturedOrDynamicActivation)]
    [InlineData(false, true, false, (int)UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency)]
    [InlineData(false, false, true, (int)UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency)]
    public void Evaluate_ActivationDependencies_DeclineBeforeCompile(
        bool capturedOrDynamic,
        bool argumentsDependency,
        bool dynamicLookupDependency,
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
                HasDynamicLookupDependency: dynamicLookupDependency));

        Assert.False(result.IsEligible);
        Assert.Equal((UnifiedBytecodeProductionDeclineCode)expectedCode, result.Code);
    }

    [Theory]
    [InlineData("arrow lexical this", true, false, (int)UnifiedBytecodeProductionDeclineCode.ArrowLexicalThisDependency)]
    [InlineData("class constructor activation", false, true, (int)UnifiedBytecodeProductionDeclineCode.ClassConstructorActivation)]
    public void Evaluate_OrdinarySyncActivationDescriptorBlockers_DeclineBeforeCompile(
        string blocker,
        bool arrowLexicalThis,
        bool classConstructor,
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
                HasArrowLexicalThisDependency: arrowLexicalThis,
                HasClassConstructorActivation: classConstructor));

        Assert.False(result.IsEligible);
        Assert.Equal((UnifiedBytecodeProductionDeclineCode)expectedCode, result.Code);
        Assert.Contains(blocker.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_FunctionDeclarationInstruction_AcceptsHoistedNoOp()
    {
        var plan = GetFunctionPlan("""
            function outer() {
                function inner() {
                    return 1;
                }

                return 2;
            }
            """,
            "outer");
        Assert.Contains(plan.Instructions, static instruction => instruction is FunctionDeclarationInstruction);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.DoesNotContain(result.Program.Instructions, static instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeclareFunction);
    }

    [Fact]
    public void Evaluate_BlockScopedFunctionDeclaration_AcceptsDeclareFunctionAndScopeOwner()
    {
        var plan = GetFunctionPlan("""
            function outer() {
                {
                    function inner() {
                        return 1;
                    }
                }

                return inner();
            }
            """,
            "outer");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, static instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PushEnvironment);
        Assert.Contains(result.Program.Instructions, static instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeclareFunction);
    }

    [Fact]
    public void Evaluate_BlockScopedFunctionDeclarationCapturingActivation_Declines()
    {
        var plan = GetFunctionPlan("""
            function outer(value) {
                {
                    function inner() {
                        return value;
                    }
                }

                return 0;
            }
            """,
            "outer");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("captures activation binding 'value'", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_SyntheticIfBranchFunctionDeclaration_AcceptsDeclareFunctionAndScopeOwner()
    {
        var plan = GetFunctionPlan("""
            function outer(flag) {
                var after = 0;
                function pick() {
                    return 0;
                }

                if (flag)
                    function pick() {
                        return 1;
                    }

                after = pick();
                return after;
            }
            """,
            "outer");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, static instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeclareFunction);
    }

    [Fact]
    public async Task Execute_BlockScopedFunctionDeclaration_RoutesAndAppliesAnnexBVarUpdate()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function outer() {
                {
                    function inner() {
                        return 1;
                    }
                }

                return inner();
            }

            outer();
        """);

        Assert.Equal(1d, result);
        AssertProductionRouted("outer");
    }

    [Fact]
    public async Task Execute_BlockScopedUsingDisposeThrow_RoutesThroughCatch()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function outer(resource) {
                try {
                    {
                        using value = resource;
                    }

                    return 'after';
                } catch (e) {
                    return e.message;
                }
            }

            outer({ [Symbol.dispose]() { throw new Error('dispose'); } });
            """);

        Assert.Equal("dispose", result);
        AssertProductionRouted("outer");
    }

    [Fact]
    public async Task Execute_BlockScopedUsingBodyThrow_DisposesBeforeCatch()
    {
        await using var engine = CreateEngine();
        var plan = GetFunctionPlan("""
            function outer(resource, target) {
                try {
                    {
                        using registered = resource;
                        target.missing();
                    }
                } catch (e) {
                    return 'caught';
                }
            }
            """,
            "outer");
        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());
        Assert.True(result.IsEligible, result.Reason);

        await engine.Evaluate("var log = [];");
        var resource = JsValue.FromObjectUnsafe(Assert.IsAssignableFrom<IAsJsValue>(
            await engine.Evaluate("({ [Symbol.dispose]() { log.push('disposed'); } })")));
        var context = engine.RealmState.CreateContext();
        var slots = new JsValue[Math.Max(result.Program.SlotCount, 1)];
        slots.AsSpan(0, result.Program.SlotCount).Fill(JsValue.Undefined);
        SetSlot(result.Program, slots, "resource", resource);
        SetSlot(result.Program, slots, "target", JsValue.Null);

        _ = UnifiedBytecodeVirtualMachine.Execute(
            result.Program,
            slots,
            context,
            engine.GlobalEnvironment);

        var log = await engine.Evaluate("log.join(',');");
        Assert.False(context.IsThrow);
        Assert.Equal("disposed", log);
    }

    [Fact]
    public async Task Execute_SyntheticIfBranchFunctionDeclaration_UpdatesAnnexBBinding()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function outer(flag) {
                var before = pick();
                function pick() {
                    return 0;
                }

                if (flag)
                    function pick() {
                        return 1;
                    }

                return before * 10 + pick();
            }

            outer(true);
            """);

        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Execute_BlockFunctionDeclarationBlockedByBodyLexicalName_DoesNotApplyAnnexBUpdate()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var init, after;

            function outer() {
                let f = 123;
                init = f;

                {
                    function f() {
                    }
                }

                after = f;
                return init * 10 + after;
            }

            outer();
            """);

        Assert.Equal(1353d, result);
        AssertProductionRouted("outer");
    }

    [Fact]
    public async Task Execute_StrictBlockFunctionDeclaration_DoesNotLeakOutsideBlock()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function outer() {
                'use strict';
                {
                    function inner() {
                        return 1;
                    }
                }

                try {
                    return inner();
                } catch (e) {
                    return e instanceof ReferenceError ? 7 : 8;
                }
            }

            outer();
            """);

        Assert.Equal(7d, result);
    }

    [Fact]
    public void Evaluate_ParameterVarDeclarationWithoutInitializer_AcceptsHoistedNoOp()
    {
        var plan = GetFunctionPlan("""
            function parameterVar(value) {
                var value;
                return value;
            }
            """,
            "parameterVar");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.DoesNotContain(result.Program.Instructions, static instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.InitializeSlot);
    }

    [Fact]
    public void Evaluate_FinalRestParameterPlan_Accepts()
    {
        var plan = GetFunctionPlan("""
            function pick(prefix, ...items) {
                return items;
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
    public void Evaluate_FinalRestArgumentsLength_AcceptsImplicitArgumentsObjectPropertyRead()
    {
        var plan = GetFunctionPlan("""
            function inspect(prefix, ...items) {
                return (arguments.length, items);
            }
            """,
            "inspect");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true,
                AllowsImplicitArgumentsObjectPropertyReadOperands: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
    }

    [Fact]
    public void Evaluate_FinalRestArgumentsIndexRead_AcceptsImplicitArgumentsObjectPropertyRead()
    {
        var plan = GetFunctionPlan("""
            function inspect(prefix, ...items) {
                return arguments[0];
            }
            """,
            "inspect");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true,
                AllowsImplicitArgumentsObjectPropertyReadOperands: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
    }

    [Theory]
    [InlineData(
        "arguments++; return arguments;",
        nameof(UnifiedBytecodeOpCode.UpdateDynamicIdentifier))]
    [InlineData(
        "return delete arguments;",
        nameof(UnifiedBytecodeOpCode.DeleteDynamicIdentifier))]
    [InlineData(
        "return arguments();",
        nameof(UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget))]
    [InlineData(
        "arguments; arguments = 7; return arguments;",
        nameof(UnifiedBytecodeOpCode.StoreDynamicIdentifierReference))]
    public void Evaluate_FinalRestArgumentsDynamicOperation_AcceptsOwnedOpcode(
        string body,
        string expectedOpCodeName)
    {
        var plan = GetFunctionPlan($$"""
            function inspect(prefix, ...items) {
                {{body}}
            }
            """,
            "inspect");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode.ToString() == expectedOpCodeName);
    }

    [Fact]
    public void Evaluate_SimpleLiteralDefaultParameterPlan_Accepts()
    {
        var plan = GetFunctionPlan("""
            function pick(value = 42) {
                return value;
            }
            """,
            "pick");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.False(result.Program.ParameterSlotIndices.IsDefaultOrEmpty);
    }

    [Fact]
    public void Evaluate_FoldedLiteralDefaultParameterPlan_Accepts()
    {
        var plan = GetFunctionPlan("""
            function pick(value = 40 + 2) {
                return value;
            }
            """,
            "pick");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.False(result.Program.ParameterSlotIndices.IsDefaultOrEmpty);
    }

    [Fact]
    public void Evaluate_ClassDeclarationInstruction_AcceptsDescriptorOpcode()
    {
        var plan = GetFunctionPlan("""
            function outer() {
                class Local {
                }

                return 1;
            }
            """,
            "outer");
        Assert.Contains(plan.Instructions, static instruction => instruction is ClassDeclarationInstruction);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void Execute_ClassDeclarationInstruction_RunsClassDefinitionSideEffectsInProductionVm()
    {
        var plan = GetFunctionPlan("""
            function outer(seed) {
                class Local {
                    static {
                        seed = seed + 1;
                    }
                }

                return seed;
            }
            """,
            "outer");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());
        Assert.True(result.IsEligible, result.Reason);

        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var slots = new JsValue[Math.Max(result.Program.SlotCount, 1)];
        slots.AsSpan(0, result.Program.SlotCount).Fill(JsValue.Undefined);
        SetSlot(result.Program, slots, "seed", JsValue.FromDouble(41));
        engine.GlobalEnvironment.DefineJsValue(Symbol.Intern("seed"), JsValue.FromDouble(41));

        var vmResult = UnifiedBytecodeVirtualMachine.Execute(
            result.Program,
            slots,
            context,
            engine.GlobalEnvironment);

        Assert.Equal(42d, vmResult.AsDouble());
    }

    [Fact]
    public void Evaluate_TryCatchPlan_AcceptsOwnedExceptionRegionOpcodes()
    {
        var plan = GetFunctionPlan("""
            function recover() {
                try {
                    throw 40;
                } catch (e) {
                    return e + 2;
                }
            }
            """,
            "recover");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnterTry);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnterCatch);
    }

    [Fact]
    public void EvaluateResumable_TryCatchPlan_AcceptsOwnedCatchOpcode()
    {
        var plan = GetFunctionPlan("""
            function* recover() {
                try {
                    throw 40;
                } catch (e) {
                    yield e + 2;
                }
            }
            """,
            "recover");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnterTry);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnterCatch);
    }

    [Fact]
    public void Evaluate_TryFinallyPlan_AcceptsOwnedFinallyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function run() {
                var value = 0;
                try {
                    value = 1;
                } finally {
                    value = 2;
                }

                return value;
            }
            """,
            "run");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnterTry);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.LeaveTry);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.EndFinally);
    }

    [Fact]
    public void Evaluate_IdentifierCallExpressionPlan_AcceptsExecutableInvocationBoundary()
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

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareIdentifierCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithNamedPropertyReadArgument_AcceptsExecutableInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box) {
                return fn(box.value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareIdentifierCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithComputedPropertyReadArgument_AcceptsExecutableInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box) {
                return fn(box["value"]);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareIdentifierCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Theory]
    [InlineData("function invoke(fn, x) { return fn(-x); }", (int)UnifiedBytecodeOpCode.UnaryMinus)]
    [InlineData("function invoke(fn, x) { return fn(+x); }", (int)UnifiedBytecodeOpCode.UnaryPlus)]
    [InlineData("function invoke(fn, x) { return fn(!x); }", (int)UnifiedBytecodeOpCode.UnaryLogicalNot)]
    [InlineData("function invoke(fn, x) { return fn(~x); }", (int)UnifiedBytecodeOpCode.UnaryBitwiseNot)]
    [InlineData("function invoke(fn, x) { return fn(void x); }", (int)UnifiedBytecodeOpCode.UnaryVoid)]
    public void Evaluate_IdentifierCallWithUnaryOperandArgument_AcceptsUnaryOpcode(string source, int expectedOpCode)
    {
        var plan = GetFunctionPlan(source, "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == (UnifiedBytecodeOpCode)expectedOpCode);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithTypeOfIdentifierArgument_AcceptsTypeOfIdentifier()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, x) {
                return fn(typeof x);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.TypeOfIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_BlockScopedIdentifierCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn) {
                var result = 0;
                {
                    let x = 1;
                    result = x;
                    fn();
                }

                return result;
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PushEnvironment);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        var callTarget = Assert.Single(result.Program.CallTargetConstants);
        Assert.Equal("fn", result.Program.SlotNames[callTarget.SlotIndex]);
    }

    [Fact]
    public void Evaluate_NamedMemberCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(box, value) {
                return box.read(value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        var callTarget = Assert.Single(result.Program.CallTargetConstants);
        Assert.Equal(UnifiedBytecodeCallTargetKind.NamedMember, callTarget.Kind);
        Assert.Equal("read", result.Program.StringConstants[callTarget.NameConstantIndex]);
    }

    [Fact]
    public void Evaluate_NamedMemberCallWithNamedPropertyReadArgument_AcceptsExecutableInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(sink, box) {
                return sink.add(box.value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithOptionalNamedPropertyReadArgument_AcceptsGetNamedPropertyAndCallBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box) {
                return fn(box?.value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyOptional);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareIdentifierCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_NamedMemberCallWithOptionalNamedPropertyReadArgument_AcceptsGetNamedPropertyAndCallBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(sink, box) {
                return sink.add(box?.value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyOptional);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithChainedOptionalNamedPropertyReadArgument_AcceptsGetNamedPropertyChain()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box) {
                return fn(box?.value?.nested);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(2, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined));
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithOptionalNamedReadChainArgument_AcceptsGetNamedPropertyChain()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box) {
                return fn(box?.child.value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_NamedMemberCallWithOptionalNamedReadChainArgument_AcceptsGetNamedPropertyChain()
    {
        var plan = GetFunctionPlan("""
            function invoke(sink, box) {
                return sink.add(box?.child.nested.deep);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(1, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined));
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithOptionalThenOptionalNamedReadChainArgument_AcceptsGetNamedPropertyChain()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box) {
                return fn(box?.child?.value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(2, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined));
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithOptionalComputedPropertyReadArgument_AcceptsGetComputedProperty()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box, key) {
                return fn(box?.[key]);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithOptionalComputedBinaryKeyArgument_AcceptsGetComputedProperty()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box, a, b) {
                return fn(box?.[a + b]);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithChainedOptionalComputedReadArgument_AcceptsComputedReadChain()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box, key) {
                return fn(box?.[key]?.[key]);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(2, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined));
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithOptionalComputedThenNamedReadChainArgument_AcceptsGetComputedAndNamedReads()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box, key) {
                return fn(box?.[key].value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithOptionalComputedThenDeepNamedReadChainArgument_Accepts()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box, key) {
                return fn(box?.[key].a.b);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithOptionalComputedThenOptionalNamedReadArgument_AcceptsNamedContinuation()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box, key) {
                return fn(box?.[key]?.value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(2, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined));
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithOptionalNamedThenComputedReadArgument_AcceptsGetComputedProperty()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box, key) {
                return fn(box?.prop[key]);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithOptionalNamedThenComputedBinaryKeyArgument_AcceptsGetComputedProperty()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box, a, b) {
                return fn(box?.prop[a + b]);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Binary);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithDeepOptionalNamedThenComputedReadArgument_Accepts()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box, key) {
                return fn(box?.a.b[key]);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
    }

    [Fact]
    public void Evaluate_IdentifierCallWithOptionalNamedThenOptionalComputedReadArgument_AcceptsComputedContinuation()
    {
        var plan = GetFunctionPlan("""
            function invoke(fn, box, key) {
                return fn(box?.prop?.[key]);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(2, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined));
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_NestedNamedMemberCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(root, value) {
                return root.child.read(value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Equal(new[] { "child", "read" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_DeeperNamedMemberCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(root, value) {
                return root.child.branch.leaf.read(value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(
            3,
            result.Program.Instructions.Count(instruction =>
                instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty));
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Equal(new[] { "child", "branch", "leaf", "read" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_ComputedMemberCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(box, key, value) {
                return box[key](value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        var callTarget = Assert.Single(result.Program.CallTargetConstants);
        Assert.Equal(UnifiedBytecodeCallTargetKind.ComputedMember, callTarget.Kind);
    }

    [Fact]
    public void Evaluate_NamedMemberCallExpressionPlan_WithDynamicArrayArgument_AcceptsExecutableInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(box) {
                return box.read([externalValue]);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_ComputedMemberCallExpressionPlan_WithDynamicArrayArgument_AcceptsExecutableInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(box, key) {
                return box[key]([externalValue]);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_ReceiverOptionalNamedCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        // gh2689: receiver-optional named call box?.read(value) is admitted.
        var plan = GetFunctionPlan("""
            function invoke(box, value) {
                return box?.read(value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CalleeOptionalNamedCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        // gh2689: callee-optional named call box.read?.(value) is admitted.
        var plan = GetFunctionPlan("""
            function invoke(box, value) {
                return box.read?.(value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CalleeOptionalComputedCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        // gh2689: callee-optional computed call box[key]?.(value) is admitted.
        var plan = GetFunctionPlan("""
            function invoke(box, key, value) {
                return box[key]?.(value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedOptionalCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_OptionalChainPlainCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        // gh2806 AC-2: a?.b.c(args) optional-start chain, plain non-optional call is admitted.
        var plan = GetFunctionPlan("""
            function invoke(a, value) {
                return a?.box.read(value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_OptionalChainReceiverOptionalCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        // gh2806 AC-3: a?.b?.c(args) double-optional chain, receiver-optional call is admitted.
        var plan = GetFunctionPlan("""
            function invoke(a, value) {
                return a?.box?.read(value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_OptionalChainComputedPlainCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        // gh2828 AC-1: a?.b[k](value) optional-start chain, computed plain call is admitted.
        var plan = GetFunctionPlan("""
            function invoke(a, key, value) {
                return a?.box[key](value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_OptionalChainComputedPlainSpreadCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(a, key, args) {
                return a?.box[key](...args);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.NotEmpty(result.Program.CallSpreadMasks);
    }

    [Fact]
    public void Evaluate_OptionalChainNamedPrefixPlainCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        // Named prefixes before the optional hop are already VM-owned receiver-chain reads.
        var plan = GetFunctionPlan("""
            function invoke(a, value) {
                return a.x?.box.read(value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_SpreadIdentifierCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        // gh2676: synchronous spread call f(...args) is admitted to the production pipeline.
        var plan = GetFunctionPlan("""
            function invoke(fn, args) {
                return fn(...args);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareIdentifierCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.NotEmpty(result.Program.CallSpreadMasks);
    }

    [Fact]
    public void Evaluate_MultiSpreadIdentifierCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        // gh2676: f(...a, ...b).
        var plan = GetFunctionPlan("""
            function invoke(fn, left, right) {
                return fn(...left, ...right);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.NotEmpty(result.Program.CallSpreadMasks);
    }

    [Fact]
    public void Evaluate_MixedSpreadIdentifierCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        // gh2676: mixed positional/spread f(a, ...b, c).
        var plan = GetFunctionPlan("""
            function invoke(fn, head, tail) {
                return fn(head, ...tail, 1);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.NotEmpty(result.Program.CallSpreadMasks);
    }

    [Fact]
    public void Evaluate_NamedMemberSpreadCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        // gh2676: obj.method(...args).
        var plan = GetFunctionPlan("""
            function invoke(box, args) {
                return box.read(...args);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.NotEmpty(result.Program.CallSpreadMasks);
    }

    [Fact]
    public void Evaluate_SpreadConstructExpressionPlan_AcceptsConstructInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(ctor, args) {
                return new ctor(...args);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ConstructInvocationBoundary);
        Assert.NotEmpty(result.Program.CallSpreadMasks);
    }

    [Fact]
    public void Evaluate_SpreadConstructResultNamedRead_AcceptsConstructInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invoke(ctor, args) {
                return new ctor(...args).x;
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ConstructInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.NotEmpty(result.Program.CallSpreadMasks);
    }

    [Fact]
    public void Evaluate_IdentifierConstructExpressionPlan_AcceptsConstructInvocationBoundary()
    {
        // gh2690: synchronous non-spread `new F(...)` is admitted to the production pipeline.
        var plan = GetFunctionPlan("""
            function make(ctor, a, b) {
                return new ctor(a, b);
            }
            """,
            "make");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ConstructInvocationBoundary);
    }

    [Fact]
    public void Evaluate_ZeroArgConstructExpressionPlan_AcceptsConstructInvocationBoundary()
    {
        // gh2690: `new F()` with no arguments.
        var plan = GetFunctionPlan("""
            function make(ctor) {
                return new ctor();
            }
            """,
            "make");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ConstructInvocationBoundary);
    }

    [Fact]
    public void Evaluate_MemberTargetConstructExpressionPlan_AcceptsConstructInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function make(box) {
                return new box.Ctor();
            }
            """,
            "make");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ConstructInvocationBoundary);
    }

    [Fact]
    public void Evaluate_ComputedMemberTargetSpreadConstructExpressionPlan_AcceptsConstructInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function make(box, key, args) {
                return new box[key](...args);
            }
            """,
            "make");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ConstructInvocationBoundary);
        Assert.NotEmpty(result.Program.CallSpreadMasks);
    }

    [Fact]
    public void Evaluate_SuperConstructExpressionPlan_AcceptsSuperConstructInvocationBoundary()
    {
        var plan = GetClassConstructorPlan("""
            class Base {
                constructor(x) { this.x = x; }
            }
            class Derived extends Base {
                constructor(x) { super(x); }
            }
            """,
            "Derived");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SuperConstructInvocationBoundary);
    }

    [Fact]
    public void Evaluate_DerivedThisAssignmentAfterSuper_AcceptsOwnedPropertyWrite()
    {
        var plan = GetClassConstructorPlan("""
            class Base {
            }
            class Derived extends Base {
                constructor(value) {
                    super();
                    this.value = value;
                }
            }
            """,
            "Derived");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SuperConstructInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadThis);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
    }

    [Fact]
    public void Evaluate_OptionalIdentifierSpreadCallExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        // Activation-slot optional identifier spread calls skip argument lowering when nullish.
        var plan = GetFunctionPlan("""
            function invoke(fn, args) {
                return fn?.(...args);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareIdentifierOptionalCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.NotEmpty(result.Program.CallSpreadMasks);
    }

    [Fact]
    public void Evaluate_DynamicOptionalIdentifierCallTarget_AcceptsEnvironmentOpcode()
    {
        var plan = GetFunctionPlan("""
            function invoke(value) {
                return externalFn?.(value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareDynamicIdentifierOptionalCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_DynamicOptionalIdentifierCallTarget_WithSimpleBinaryArgument_AcceptsEnvironmentOpcode()
    {
        var plan = GetFunctionPlan("""
            function invoke(value) {
                return externalFn?.(value + 1);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareDynamicIdentifierOptionalCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Binary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_DynamicOptionalIdentifierCallTarget_WithLiteralStartSimpleBinaryArgument_AcceptsEnvironmentOpcode()
    {
        var plan = GetFunctionPlan("""
            function invoke(value) {
                return externalFn?.(1 + value);
            }
            """,
            "invoke");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareDynamicIdentifierOptionalCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Binary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_NamedPropertyReadCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function read(box) {
                return box.value;
            }
            """,
            "read");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Equal("value", Assert.Single(result.Program.StringConstants));
    }

    [Fact]
    public void Evaluate_NamedPropertyReadWithDynamicIdentifierBase_AcceptsWhenDynamicReadsAreAdmitted()
    {
        var plan = GetFunctionPlan("""
            function read() {
                return outer.value;
            }
            """,
            "read");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Equal(new[] { "outer", "value" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_ComputedPropertyReadWithDynamicIdentifierBase_AcceptsWhenDynamicReadsAreAdmitted()
    {
        var plan = GetFunctionPlan("""
            function read(key) {
                return outer[key];
            }
            """,
            "read");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Equal(new[] { "outer" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_TwoHopNamedPropertyReadCandidate_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function read(box) {
                return box.child.value;
            }
            """,
            "read");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(
            2,
            result.Program.Instructions.Count(instruction =>
                instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty));
        Assert.Equal(new[] { "child", "value" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_DeeperNamedPropertyReadCandidate_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function read(box) {
                return box.child.branch.leaf.value;
            }
            """,
            "read");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(
            4,
            result.Program.Instructions.Count(instruction =>
                instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty));
        Assert.Equal(new[] { "child", "branch", "leaf", "value" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_ThisPropertyReadCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function readThis() {
                return this.value;
            }
            """,
            "readThis");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadThis);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
    }

    [Fact]
    public void Evaluate_NewTargetReadCandidate_AcceptsLoadNewTarget()
    {
        var plan = GetFunctionPlan("""
            function readNewTarget() {
                return new.target;
            }
            """,
            "readNewTarget");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadNewTarget);
    }

    [Fact]
    public void Evaluate_ComputedPropertyReadCandidate_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function read(box, key) {
                return box[key];
            }
            """,
            "read");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.RequireObjectCoercible && instruction.Operand == 1);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
    }

    [Fact]
    public void Evaluate_ComputedPropertyReadWithRichKeyCandidate_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function read(box, left, right) {
                return box[left + right];
            }
            """,
            "read");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Binary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
    }

    [Fact]
    public void Evaluate_ComputedThenNamedPropertyReadCandidate_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function read(box, key) {
                return box[key].value;
            }
            """,
            "read");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Equal(1, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty));
    }

    [Fact]
    public void Evaluate_NamedThenComputedPropertyReadCandidate_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function read(box, key) {
                return box.child[key];
            }
            """,
            "read");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
    }

    [Fact]
    public void Evaluate_NamedPropertyWriteCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function write(box, value) {
                return box.value = value;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Equal("value", Assert.Single(result.Program.StringConstants));
    }

    [Fact]
    public void Evaluate_NamedPropertyWriteWithDynamicIdentifierBase_AcceptsWhenDynamicReadsAreAdmitted()
    {
        var plan = GetFunctionPlan("""
            function write(value) {
                return outer.value = value;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Equal(new[] { "outer", "value" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_ComputedPropertyWriteCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function write(box, key, value) {
                return box[key] = value;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Fact]
    public void Evaluate_ComputedPropertyWriteWithDynamicIdentifierBase_AcceptsWhenDynamicReadsAreAdmitted()
    {
        var plan = GetFunctionPlan("""
            function write(key, value) {
                return outer[key] = value;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
        Assert.Equal(new[] { "outer" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_NamedPropertyDeleteCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function remove(box) {
                return delete box.value;
            }
            """,
            "remove");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteNamedProperty);
        Assert.Equal("value", Assert.Single(result.Program.StringConstants));
    }

    [Fact]
    public void Evaluate_NamedPropertyDeleteWithDynamicIdentifierBase_AcceptsWhenDynamicReadsAreAdmitted()
    {
        var plan = GetFunctionPlan("""
            function remove() {
                return delete outer.value;
            }
            """,
            "remove");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteNamedProperty);
        Assert.Equal(new[] { "outer", "value" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_NestedNamedPropertyDeleteCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function remove(box) {
                return delete box.child.value;
            }
            """,
            "remove");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteNamedProperty);
    }

    [Fact]
    public void Evaluate_ComputedPropertyDeleteCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function remove(box, key) {
                return delete box[key];
            }
            """,
            "remove");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteComputedProperty);
    }

    [Fact]
    public void Evaluate_ComputedPropertyDeleteWithDynamicIdentifierBase_AcceptsWhenDynamicReadsAreAdmitted()
    {
        var plan = GetFunctionPlan("""
            function remove(key) {
                return delete outer[key];
            }
            """,
            "remove");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteComputedProperty);
        Assert.Equal(new[] { "outer" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_NestedNamedReceiverComputedPropertyDeleteCandidate_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function remove(box, key) {
                return delete box.child[key];
            }
            """,
            "remove");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteComputedProperty);
        Assert.Equal(2, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadSlot));
    }

    [Fact]
    public void Evaluate_NestedComputedPropertyDeleteRichKeyCandidate_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function remove(box, left, right) {
                return delete box.child[left + right];
            }
            """,
            "remove");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Binary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteComputedProperty);
    }

    // A25/A26: deep delete chains with a computed read hop before the terminal delete.

    [Fact]
    public void Evaluate_DeepComputedThenComputedDeleteCandidate_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function remove(box, k1, k2) {
                return delete box[k1][k2];
            }
            """,
            "remove");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteComputedProperty);
    }

    [Fact]
    public void Evaluate_NamedThenComputedThenComputedDeleteCandidate_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function remove(box, k1, k2) {
                return delete box.a[k1][k2];
            }
            """,
            "remove");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteComputedProperty);
    }

    [Fact]
    public void Evaluate_ComputedThenNamedDeleteCandidate_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function remove(box, k1) {
                return delete box[k1].b;
            }
            """,
            "remove");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteNamedProperty);
    }

    [Fact]
    public void Evaluate_DeepDeleteChainWithPrivateNamedHop_Declines()
    {
        // A private-name hop in the chain must keep its IR-runner path (PrivateFieldDependency),
        // never the deep-delete production route.
        var plan = GetClassMethodPlan("""
            class Holder {
                #child = { x: 1, y: 2 };
                remove(k) {
                    return delete this.#child[k].x;
                }
            }
            """,
            "Holder",
            "remove");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        // A private-name hop must NOT reach the deep-delete production route; it stays declined
        // (the exact decline code is incidental — the read boundary rejects the private hop first).
        Assert.False(result.IsEligible);
        Assert.NotEqual(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_OptionalComputedReadThenComputedDeleteChain_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function remove(box, k1, k2) {
                return delete box?.[k1][k2];
            }
            """,
            "remove");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteComputedProperty);
    }

    [Fact]
    public void Evaluate_OptionalNamedDeleteShapes_PreserveTerminalOptionality()
    {
        var terminalOptionalPlan = GetFunctionPlan("""
            function remove(box) {
                return delete box?.value?.leaf;
            }
            """,
            "remove");
        var terminalOptionalProgram = Assert.Single(
            terminalOptionalPlan.Instructions.OfType<ReturnInstruction>(),
            instruction => instruction.ReturnProgram is not null).ReturnProgram!.Value;

        Assert.Contains(
            terminalOptionalProgram.GetOps(),
            op => op.Kind == ExpressionOpKind.JumpIfNullish);
        Assert.DoesNotContain(
            terminalOptionalProgram.GetOps(),
            op => op.Kind == ExpressionOpKind.JumpIfShortCircuited);

        var nonTerminalOptionalPlan = GetFunctionPlan("""
            function remove(box) {
                return delete box?.value.leaf;
            }
            """,
            "remove");
        var nonTerminalOptionalProgram = Assert.Single(
            nonTerminalOptionalPlan.Instructions.OfType<ReturnInstruction>(),
            instruction => instruction.ReturnProgram is not null).ReturnProgram!.Value;

        Assert.Contains(
            nonTerminalOptionalProgram.GetOps(),
            op => op.Kind == ExpressionOpKind.JumpIfShortCircuited);
        Assert.DoesNotContain(
            nonTerminalOptionalProgram.GetOps(),
            op => op.Kind == ExpressionOpKind.JumpIfNullish);
    }

    [Theory]
    [InlineData(
        """
        function remove(box) {
            return delete box?.value;
        }
        """,
        "remove")]
    [InlineData(
        """
        function remove(box) {
            return delete box.child?.value;
        }
        """,
        "remove")]
    [InlineData(
        """
        function remove(box) {
            return delete box?.value?.leaf;
        }
        """,
        "remove")]
    public void Evaluate_OptionalNamedPropertyDeleteCandidate_AcceptsOwnedPropertyOpcodes(
        string source,
        string functionName)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteNamedProperty);
    }

    [Fact]
    public void Evaluate_NonTerminalOptionalNamedPropertyDelete_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function remove(box) {
                return delete box?.value.leaf;
            }
            """,
            "remove");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyOptional);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuited);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteNamedProperty);
    }

    [Theory]
    [InlineData(
        """
        function remove(box, key) {
            return delete box?.[key];
        }
        """,
        "remove")]
    [InlineData(
        """
        function remove(box, key) {
            return delete box?.child[key];
        }
        """,
        "remove")]
    [InlineData(
        """
        function remove(box, key) {
            return delete box.child?.[key];
        }
        """,
        "remove")]
    [InlineData(
        """
        function remove(box, key) {
            return delete box?.child?.[key];
        }
        """,
        "remove")]
    public void Evaluate_OptionalComputedPropertyDeleteCandidate_AcceptsOwnedPropertyOpcodes(
        string source,
        string functionName)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode is
                UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined or
                UnifiedBytecodeOpCode.JumpIfShortCircuited);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteComputedProperty);
    }

    [Fact]
    public void Evaluate_NamedCompoundPropertyWriteCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function write(box, value) {
                return box.value += value;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Equal("value", Assert.Single(result.Program.StringConstants));
    }

    [Fact]
    public void Evaluate_NamedCompoundPropertyWriteWithDynamicIdentifierBase_AcceptsWhenDynamicReadsAreAdmitted()
    {
        var plan = GetFunctionPlan("""
            function write() {
                return outer.value += 2;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Equal(new[] { "outer", "value" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_ComputedCompoundPropertyWriteCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function write(box, key, value) {
                return box[key] += value;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Fact]
    public void Evaluate_ComputedCompoundPropertyWriteWithDynamicIdentifierBase_AcceptsWhenDynamicReadsAreAdmitted()
    {
        var plan = GetFunctionPlan("""
            function write() {
                return outer[key] += 2;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
        Assert.Equal(new[] { "outer", "key" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_ComputedCompoundPropertyWriteWithExpressionKey_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function write(box, key, suffix, value) {
                return box[key + suffix] += value;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: (int)BinaryOperator.Add });
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Theory]
    [MemberData(nameof(CompoundNamedPropertyWriteOperators))]
    public void Evaluate_NamedCompoundPropertyWrite_AcceptsForEachProductionOperator(
        string functionName,
        string op)
    {
        var plan = GetFunctionPlan($$"""
            function {{functionName}}(box, value) {
                return box.prop {{op}} value;
            }
            """,
            functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
    }

    [Theory]
    [MemberData(nameof(CompoundNamedPropertyWriteOperators))]
    public void Evaluate_ComputedCompoundPropertyWrite_AcceptsForEachProductionOperator(
        string functionName,
        string op)
    {
        var plan = GetFunctionPlan($$"""
            function {{functionName}}(box, key, value) {
                return box[key] {{op}} value;
            }
            """,
            functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Fact]
    public void Evaluate_NamedPropertyUpdateCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function update(box) {
                return ++box.value;
            }
            """,
            "update");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateNamedProperty);
        Assert.Equal("value", Assert.Single(result.Program.StringConstants));
    }

    [Fact]
    public void Evaluate_NamedPropertyUpdateWithDynamicIdentifierBase_AcceptsWhenDynamicReadsAreAdmitted()
    {
        var plan = GetFunctionPlan("""
            function update() {
                return outer.value++;
            }
            """,
            "update");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateNamedProperty);
        Assert.Equal(new[] { "outer", "value" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_ComputedPropertyUpdateCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function update(box, key) {
                return box[key]++;
            }
            """,
            "update");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateComputedProperty);
    }

    [Fact]
    public void Evaluate_ComputedPropertyUpdateWithDynamicIdentifierBase_AcceptsWhenDynamicReadsAreAdmitted()
    {
        var plan = GetFunctionPlan("""
            function update(key) {
                return outer[key]++;
            }
            """,
            "update");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateComputedProperty);
        Assert.Equal(new[] { "outer" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_ComputedPropertyUpdateWithExpressionKey_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function update(box, key, suffix) {
                return box[key + suffix]++;
            }
            """,
            "update");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: (int)BinaryOperator.Add });
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateComputedProperty);
    }

    [Fact]
    public void Evaluate_EmptyReturnCandidate_AcceptsReturnUndefinedOpcode()
    {
        var plan = GetFunctionPlan("""
            function explicitEmpty() {
                return;
            }
            """,
            "explicitEmpty");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ReturnUndefined);
    }

    [Fact]
    public void Evaluate_ImplicitReturnCandidate_AcceptsReturnUndefinedOpcode()
    {
        var plan = GetFunctionPlan("""
            function implicitReturn(value) {
                var local = value;
            }
            """,
            "implicitReturn");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ReturnUndefined);
    }

    [Fact]
    public void Evaluate_ThrowCandidate_AcceptsThrowOpcode()
    {
        var plan = GetFunctionPlan("""
            function fail(value) {
                throw value;
            }
            """,
            "fail");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Throw);
    }

    [Fact]
    public void Evaluate_DiscardedPropertyWriteCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function writeDiscarded(box, value) {
                box.value = value;
                return box.value;
            }
            """,
            "writeDiscarded");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Pop);
    }

    [Fact]
    public void Evaluate_DiscardedPropertyUpdateCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetFunctionPlan("""
            function updateDiscarded(box) {
                box.value++;
                return box.value;
            }
            """,
            "updateDiscarded");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Pop);
    }

    [Fact]
    public void Evaluate_ArrayLiteralCandidate_AcceptsLiteralConstructionOpcodes()
    {
        var plan = GetFunctionPlan("""
            function create(value) {
                return [1, , [value]];
            }
            """,
            "create");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CreateArray);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArrayPushHole);
    }

    [Fact]
    public void Evaluate_ObjectLiteralCandidate_AcceptsStaticAndComputedDataProperties()
    {
        var plan = GetFunctionPlan("""
            function create(key, value) {
                var nested = { child: value };
                return { a: 1, [key]: nested };
            }
            """,
            "create");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CreateObject);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
    }

    [Fact]
    public void Evaluate_SimpleBlockLexicalScope_AcceptsPushPopAndFlatScopedSlots()
    {
        var plan = GetFunctionPlan("""
            function scoped(value) {
                var result = value;
                {
                    let value = 5;
                    const next = value + 1;
                    result = next;
                }

                return result + value;
            }
            """,
            "scoped");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PushEnvironment);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PopEnvironment);
        Assert.True(result.Program.SlotCount >= plan.FlatSlotCount);
    }

    [Fact]
    public void Evaluate_IntegratedCompletedLaneProgram_AcceptsAsOneExecutableProgram()
    {
        var plan = GetFunctionPlan("""
            function integrated(box, n, key, seed) {
                var total = seed;
                var values = [1, , n];
                var record = { first: 1, [key]: n };
                {
                    let local = record[key];
                    const currentRaw = box.value;
                    const current = +currentRaw;
                    total = total + local + current;
                }

                while (n > 0) {
                    total = total + n;
                    n = n - 1;
                }

                box.value = total;
                var count = ++box.count;
                var stored = box.value;
                return stored + count + 1 + 3;
            }
            """,
            "integrated");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.CreateArray);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.CreateObject);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.PushEnvironment);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateNamedProperty);
        Assert.DoesNotContain(result.Program.Instructions, instruction =>
            instruction.OpCode is UnifiedBytecodeOpCode.PrepareIdentifierCallTarget
                or UnifiedBytecodeOpCode.PrepareNamedCallTarget
                or UnifiedBytecodeOpCode.PrepareComputedCallTarget
                or UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Theory]
    [InlineData("var value = 1")]
    [InlineData("let value = 1")]
    [InlineData("const value = 1")]
    [InlineData("function value() { return 1; }")]
    [InlineData("class value {}")]
    public void Evaluate_DirectEvalDeclarationLiteral_DeclinesEvalInjectedRuntimeBinding(string evalSource)
    {
        var plan = GetFunctionPlan($$"""
            function directEval() {
                eval("{{evalSource}}");
                return value;
            }
            """,
            "directEval");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.CallDependency, result.Code);
        Assert.Contains("Direct eval invocation semantics", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithDynamicIdentifierLoad_AcceptsBoundedDynamicNameProgram()
    {
        var plan = GetFunctionPlan("""
            function dynamic(box) {
                with (box) {
                    return value;
                }
            }
            """,
            "dynamic");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnterWith);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.Return);
    }

    [Fact]
    public void Evaluate_WithDynamicIdentifierOperations_AcceptsBoundedDynamicNameProgram()
    {
        var plan = GetFunctionPlan("""
            function dynamic(box) {
                with (box) {
                    value = value + 2;
                    ++count;
                    missingType = typeof missing;
                    deleteResult = delete removable;
                    removableType = typeof removable;
                    return value + count;
                }
            }
            """,
            "dynamic");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.StoreDynamicIdentifierReference);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.TypeOfDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeleteDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_WithThenOutsideDynamicIdentifier_AcceptsMixedDynamicNameProgram()
    {
        var plan = GetFunctionPlan("""
            function dynamic(box) {
                with (box) {
                    value = value + 1;
                }

                return externalValue + 1;
            }
            """,
            "dynamic");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnterWith);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_WithAroundTryCatchFinally_PropagatesDynamicDepthIntoHandlerAndFinally()
    {
        var plan = GetFunctionPlan("""
            function dynamic(box) {
                with (box) {
                    try {
                        throw 1;
                    } catch {
                        value = value + 1;
                    } finally {
                        value = value + 2;
                    }

                    return value;
                }
            }
            """,
            "dynamic");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnterWith);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnterTry);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnterCatch);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.EndFinally);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.StoreDynamicIdentifierReference);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
    }

    [Fact]
    public void EvaluateResumable_WithDynamicIdentifierLoad_AcceptsBoundedCurrentEnvironment()
    {
        var plan = GetFunctionPlan("""
            function* dynamic(box) {
                with (box) {
                    yield value;
                }
            }
            """,
            "dynamic");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnterWith);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.LeaveWith);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
    }

    [Fact]
    public void EvaluateResumable_AwaitedWithObject_DeclinesD3Residue()
    {
        var plan = GetFunctionPlan("""
            async function dynamic(scopePromise) {
                with (await scopePromise) {
                    return value;
                }
            }
            """,
            "dynamic");

        Assert.Contains(plan.Instructions, static instruction =>
            instruction is EnterWithInstruction { AwaitedProgram: not null });

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("awaited with-object evaluation", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_UnreachableWithBody_DoesNotDeclineD3Residue()
    {
        var plan = GetFunctionPlan("""
            function* dynamic(box) {
                return 1;
                with (box) {
                    yield value;
                }
            }
            """,
            "dynamic");

        Assert.Contains(plan.Instructions, static instruction => instruction is EnterWithInstruction);

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_SimpleSourceArraySpread_Accepts()
    {
        var plan = GetFunctionPlan("""
            function spreadArray(source) {
                return [1, ...source];
            }
            """,
            "spreadArray");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_NonSimpleSourceArraySpread_AcceptsDirectNamedCallSource()
    {
        var plan = GetFunctionPlan("""
            function spreadNonSimple(a, b) {
                return [...a.slice(0, b)];
            }
            """,
            "spreadNonSimple");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArraySpread);
    }

    [Fact]
    public void Evaluate_NonSimpleSourceArraySpread_WithDynamicDirectNamedCallSource_Accepts()
    {
        var plan = GetFunctionPlan("""
            function spreadNonSimple() {
                return [...source.slice(0, take)];
            }
            """,
            "spreadNonSimple");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArraySpread);
    }

    [Fact]
    public void Evaluate_PureSpreadArrayLiteral_Accepts()
    {
        var plan = GetFunctionPlan("""
            function f(a) {
                return [...a];
            }
            """,
            "f");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CreateArray);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArraySpread);
    }

    [Fact]
    public void Evaluate_MixedSpreadArrayLiteral_Accepts()
    {
        var plan = GetFunctionPlan("""
            function f(a, b, c) {
                return [a, ...b, c];
            }
            """,
            "f");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CreateArray);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArraySpread);
    }

    [Fact]
    public void Evaluate_HoleThenSpreadArrayLiteral_Accepts()
    {
        var plan = GetFunctionPlan("""
            function f(a) {
                return [, ...a];
            }
            """,
            "f");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CreateArray);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArrayPushHole);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArraySpread);
    }

    [Fact]
    public void Evaluate_SimpleObjectSpreadLiteral_Accepts()
    {
        var plan = GetFunctionPlan("""
            function spreadObject(source) {
                return { a: 1, ...source, b: 2 };
            }
            """,
            "spreadObject");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ObjectSpread);
    }

    [Fact]
    public void Evaluate_NonSimpleObjectSpreadSource_AcceptsDirectNamedCallSource()
    {
        var plan = GetFunctionPlan("""
            function spreadObject(source) {
                return { ...source.slice(0) };
            }
            """,
            "spreadObject");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ObjectSpread);
    }

    [Fact]
    public void Evaluate_NonSimpleObjectSpreadSource_WithDynamicDirectNamedCallSource_Accepts()
    {
        var plan = GetFunctionPlan("""
            function spreadObject() {
                return { ...source.slice(0, take) };
            }
            """,
            "spreadObject");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ObjectSpread);
    }

    [Fact]
    public void Evaluate_NonSimpleSourceArraySpread_AcceptsDirectComputedCallSource()
    {
        var plan = GetFunctionPlan("""
            function spreadNonSimple(source, take) {
                return [...source["slice"](0, take)];
            }
            """,
            "spreadNonSimple");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArraySpread);
    }

    [Fact]
    public void Evaluate_NonSimpleSourceArraySpread_AcceptsReceiverOptionalNamedCallSource()
    {
        var plan = GetFunctionPlan("""
            function spreadNonSimple(source, take) {
                return [...source?.slice(0, take)];
            }
            """,
            "spreadNonSimple");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArraySpread);
    }

    [Fact]
    public void Evaluate_NonSimpleSourceArraySpread_AcceptsReceiverOptionalComputedCallSource()
    {
        var plan = GetFunctionPlan("""
            function spreadNonSimple(source, method, take) {
                return [...source?.[method](0, take)];
            }
            """,
            "spreadNonSimple");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArraySpread);
    }

    [Fact]
    public void Evaluate_NonSimpleSourceArraySpread_AcceptsCalleeOptionalComputedCallSource()
    {
        var plan = GetFunctionPlan("""
            function spreadNonSimple(source, method, take) {
                return [...source[method]?.(0, take)];
            }
            """,
            "spreadNonSimple");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArraySpread);
    }

    [Fact]
    public void Evaluate_ObjectPropertyValue_AcceptsDirectComputedCallValue()
    {
        var plan = GetFunctionPlan("""
            function objectValue(source, take) {
                return { part: source["slice"](0, take) };
            }
            """,
            "objectValue");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
    }

    [Fact]
    public void Evaluate_ObjectPropertyValue_AcceptsReceiverOptionalNamedCallValue()
    {
        var plan = GetFunctionPlan("""
            function objectValue(source, take) {
                return { part: source?.slice(0, take) };
            }
            """,
            "objectValue");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
    }

    [Fact]
    public void Evaluate_ObjectPropertyValue_AcceptsCalleeOptionalComputedCallValue()
    {
        var plan = GetFunctionPlan("""
            function objectValue(source, method, take) {
                return { part: source[method]?.(0, take) };
            }
            """,
            "objectValue");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
    }

    [Fact]
    public void Evaluate_CallArgumentObjectValue_AcceptsReceiverOptionalNamedCallValue()
    {
        var plan = GetFunctionPlan("""
            function callSink(sink, source, take) {
                return sink({ part: source?.slice(0, take) });
            }
            """,
            "callSink");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
    }

    [Fact]
    public void Evaluate_CallArgumentObjectValue_AcceptsReceiverOptionalComputedCallValue()
    {
        var plan = GetFunctionPlan("""
            function callSink(sink, source, method, take) {
                return sink({ part: source?.[method](0, take) });
            }
            """,
            "callSink");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
    }

    [Fact]
    public void Evaluate_CallArgumentObjectValue_AcceptsCalleeOptionalComputedCallValue()
    {
        var plan = GetFunctionPlan("""
            function callSink(sink, source, method, take) {
                return sink({ part: source[method]?.(0, take) });
            }
            """,
            "callSink");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedOptionalCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
    }

    [Fact]
    public void Evaluate_ObjectPropertyValue_WithDynamicDirectComputedCallValue_Accepts()
    {
        var plan = GetFunctionPlan("""
            function objectValue() {
                return { part: source[method](0, take) };
            }
            """,
            "objectValue");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
    }

    [Fact]
    public void Evaluate_ComputedObjectPropertyKey_AcceptsDirectComputedCallKey()
    {
        var plan = GetFunctionPlan("""
            function objectKey(source) {
                return { [source["join"]("")]: 42 };
            }
            """,
            "objectKey");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
    }

    [Fact]
    public void Evaluate_ComputedObjectPropertyKey_AcceptsCalleeOptionalNamedCallKey()
    {
        var plan = GetFunctionPlan("""
            function objectKey(source) {
                return { [source.join?.("")]: 42 };
            }
            """,
            "objectKey");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
    }

    [Fact]
    public void Evaluate_ComputedObjectPropertyKey_AcceptsCalleeOptionalComputedCallKey()
    {
        var plan = GetFunctionPlan("""
            function objectKey(source, method) {
                return { [source[method]?.("")]: 42 };
            }
            """,
            "objectKey");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
    }

    [Fact]
    public void Evaluate_ObjectPropertyValue_AcceptsDirectNamedCallValue()
    {
        var plan = GetFunctionPlan("""
            function objectValue(source, take) {
                return { part: source.slice(0, take) };
            }
            """,
            "objectValue");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
    }

    [Fact]
    public void Evaluate_ObjectPropertyValue_WithDynamicDirectNamedCallValue_Accepts()
    {
        var plan = GetFunctionPlan("""
            function objectValue() {
                return { part: source.slice(0, take) };
            }
            """,
            "objectValue");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
    }

    [Fact]
    public void Evaluate_ComputedObjectPropertyValue_AcceptsDirectNamedCallValue()
    {
        var plan = GetFunctionPlan("""
            function objectValue(source, take, key) {
                return { [key]: source.slice(0, take) };
            }
            """,
            "objectValue");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
    }

    [Fact]
    public void Evaluate_ComputedObjectPropertyKey_AcceptsDirectNamedCallKey()
    {
        var plan = GetFunctionPlan("""
            function objectKey(source, take) {
                return { [source.slice(0, take)]: 42 };
            }
            """,
            "objectKey");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
    }

    [Theory]
    [InlineData(
        """
        function methodObject() {
            return { value() { return 1; } };
        }
        """,
        "methodObject",
        (int)UnifiedBytecodeOpCode.DefineObjectMethod,
        false)]
    [InlineData(
        """
        function accessorObject() {
            return { get value() { return 1; } };
        }
        """,
        "accessorObject",
        (int)UnifiedBytecodeOpCode.DefineObjectAccessor,
        true)]
    [InlineData(
        """
        function computedMethodObject(key) {
            return { [key]() { return 1; } };
        }
        """,
        "computedMethodObject",
        (int)UnifiedBytecodeOpCode.DefineComputedObjectMethod,
        false)]
    [InlineData(
        """
        function computedAccessorObject(key) {
            return { get [key]() { return 1; } };
        }
        """,
        "computedAccessorObject",
        (int)UnifiedBytecodeOpCode.DefineComputedObjectAccessor,
        true)]
    public void Evaluate_ObjectMethodAndAccessorLiteralShapes_AcceptAndVmDefinesProperty(
        string source,
        string functionName,
        int expectedOpcode,
        bool isAccessor)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => (int)instruction.OpCode == expectedOpcode);

        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var slots = new JsValue[Math.Max(result.Program.SlotCount, 1)];
        if (result.Program.SlotNames.IndexOf("key") >= 0)
        {
            SetSlot(result.Program, slots, "key", JsValue.FromString("value"));
        }

        var vmResult = UnifiedBytecodeVirtualMachine.Execute(
            result.Program,
            slots,
            context,
            engine.GlobalEnvironment);

        var obj = Assert.IsType<JsObject>(vmResult.ObjectValue);
        var descriptor = obj.GetOwnPropertyDescriptor("value");
        Assert.NotNull(descriptor);
        if (isAccessor)
        {
            Assert.True(descriptor!.IsAccessorDescriptor);
            Assert.NotNull(descriptor.Get);
        }
        else
        {
            Assert.True(descriptor!.JsValue.TryGetObject<IJsCallable>(out _));
            Assert.True(descriptor.Writable);
        }
    }

    [Theory]
    [InlineData(
        """
        function namedFunctionValue() {
            return { value: function() {} };
        }
        """,
        "namedFunctionValue")]
    [InlineData(
        """
        function namedArrowValue(x) {
            return { handler: () => x };
        }
        """,
        "namedArrowValue")]
    [InlineData(
        """
        function computedNamedFunctionValue(key) {
            return { [key]: function() {} };
        }
        """,
        "computedNamedFunctionValue")]
    [InlineData(
        """
        function namedClassValue() {
            return { value: class {} };
        }
        """,
        "namedClassValue")]
    public void Evaluate_ObjectLiteralNameInferenceShapes_AcceptWithNoneCode(
        string source,
        string functionName)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_ClassLiteralValuePlan_AcceptsAndCompilesClassLiteralOpcode()
    {
        var plan = GetFunctionPlan("""
            function makeClass(baseValue) {
                return class extends baseValue {
                    value() { return 1; }
                };
            }
            """,
            "makeClass");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void Evaluate_ClassLiteralStaticBlockPlan_AcceptsAndCompilesClassLiteralOpcode()
    {
        var plan = GetFunctionPlan("""
            function makeClass(seed) {
                return class Box {
                    static {
                        Box.value = seed + 1;
                    }
                };
            }
            """,
            "makeClass");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void Evaluate_ClassLiteralWithComputedMemberAndFieldNames_AcceptsAndCompilesClassLiteralOpcode()
    {
        var plan = GetFunctionPlan("""
            function makeComputedClass(methodKey, instanceKey, staticKey, seed) {
                return class {
                    [methodKey]() {
                        return this[instanceKey] + this.constructor[staticKey] + seed;
                    }

                    [instanceKey] = 10;
                    static [staticKey] = 20;
                };
            }
            """,
            "makeComputedClass");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionPrivateFieldWithPublicMembers_DeclinesMixedClassMemberSlice()
    {
        var plan = GetFunctionPlan("""
            function* makeBox() {
                yield 0;
                return class {
                    #value = 42;

                    read() {
                        return this.#value;
                    }

                    has(receiver) {
                        return #value in receiver;
                    }
                };
            }
            """,
            "makeBox");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("outside B24e", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AcceptedPropertyWriteAndUpdatePrograms))]
    public void Evaluate_AcceptedPropertyWriteAndUpdatePrograms_StayWithinOwnedOpcodeSubset(
        string source,
        string functionName,
        int[] requiredOpcodes,
        int[] allowedOpcodes)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        AssertAllInstructionsUseOwnedOpcodes(result.Program, allowedOpcodes);
        foreach (var requiredOpcode in requiredOpcodes)
        {
            Assert.Contains(result.Program.Instructions, instruction => (int)instruction.OpCode == requiredOpcode);
        }
    }

    [Fact]
    public void Evaluate_DirectEvalLiteralExpressionPlan_AcceptsExecutableInvocationBoundary()
    {
        var plan = GetFunctionPlan("""
            function invokeEval() {
                return eval("'non-injecting direct eval'");
            }
            """,
            "invokeEval");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Theory]
    [InlineData("arguments[0]")]
    [InlineData("arguments.length")]
    [InlineData("local + arguments[1]")]
    public void Evaluate_DirectEvalLiteralReadsArgumentsAndActivationBinding_AcceptsExecutableInvocationBoundary(
        string evalSource)
    {
        var plan = GetFunctionPlan($$"""
            function invokeEval(first, second) {
                var local = 40;
                return eval("{{evalSource}}");
            }
            """,
            "invokeEval");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_DirectEvalMultipleArguments_DeclinesProductionRoute()
    {
        var plan = GetFunctionPlan("""
            function invokeEval(extra) {
                return eval("'non-injecting direct eval'", extra);
            }
            """,
            "invokeEval");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.CallDependency, result.Code);
        Assert.Contains("Direct eval invocation semantics", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_DirectEvalSpreadArguments_DeclinesProductionRoute()
    {
        var plan = GetFunctionPlan("""
            function invokeEval(args) {
                return eval(...args);
            }
            """,
            "invokeEval");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.CallDependency, result.Code);
        Assert.Contains("Direct eval invocation semantics", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_DirectEvalIdentifierCallOpOutsideAdmittedShape_DeclinesAsCallDependency()
    {
        var expressionProgram = new ExpressionProgram(
            ImmutableArray.Create(
                PackedExpressionOp.LoadIdentifierCallTarget(0),
                PackedExpressionOp.LoadLiteralConstant(0),
                PackedExpressionOp.LoadLiteralConstant(1),
                PackedExpressionOp.Call(ArgumentCount: 2, HasExplicitThis: false, IsDirectEval: true)),
            literalConstants: ImmutableArray.Create(JsValue.FromString("'non-injecting direct eval'"), JsValue.FromDouble(0)),
            identifierConstants: ImmutableArray.Create(new IdentifierOperand(Symbol.Eval)));
        var seedPlan = GetFunctionPlan("function invokeEval() { return 0; }", "invokeEval");
        var plan = seedPlan with
        {
            Instructions = ImmutableArray.Create<ExecutionInstruction>(new ReturnInstruction(-1, expressionProgram)),
            EntryPoint = 0
        };

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.CallDependency, result.Code);
        Assert.Contains("Direct eval invocation semantics", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_DirectEvalIdentifierSourceWithDynamicRead_DeclinesEvalInjectedRuntimeBinding()
    {
        var plan = GetFunctionPlan("""
            function invokeEval(source) {
                eval(source);
                return value;
            }
            """,
            "invokeEval");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.CallDependency, result.Code);
    }

    [Fact]
    public void Evaluate_DynamicIdentifierCallTarget_AcceptsEnvironmentOpcode()
    {
        var plan = GetFunctionPlan("""
            function invokeExternal(value) {
                return externalFn(value);
            }
            """,
            "invokeExternal");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_DynamicIdentifierCallTarget_WithDynamicArgument_AcceptsEnvironmentOpcode()
    {
        var plan = GetFunctionPlan("""
            function invokeExternal() {
                return externalFn(externalValue);
            }
            """,
            "invokeExternal");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_DynamicIdentifierCallTarget_WithSimpleBinaryDynamicArgument_AcceptsEnvironmentOpcode()
    {
        var plan = GetFunctionPlan("""
            function invokeExternal() {
                return externalFn(externalValue + 1);
            }
            """,
            "invokeExternal");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Binary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_DynamicIdentifierCallTarget_WithDynamicArrayArgument_AcceptsEnvironmentOpcode()
    {
        var plan = GetFunctionPlan("""
            function invokeExternal() {
                return externalFn([externalValue]);
            }
            """,
            "invokeExternal");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CreateArray);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_DynamicIdentifierCallTarget_WithDynamicObjectArgument_AcceptsEnvironmentOpcode()
    {
        var plan = GetFunctionPlan("""
            function invokeExternal() {
                return externalFn({ item: externalValue });
            }
            """,
            "invokeExternal");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CreateObject);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_DynamicIdentifierCallTarget_WithDynamicTemplateArgument_AcceptsEnvironmentOpcode()
    {
        var plan = GetFunctionPlan("""
            function invokeExternal() {
                return externalFn(`${externalValue}`);
            }
            """,
            "invokeExternal");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ToString);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_DiscardedIdentifierCallCandidate_AcceptsInvocationBoundaryAndPop()
    {
        var plan = GetFunctionPlan("""
            function invokeDiscarded(callback, value) {
                callback(value);
                return value;
            }
            """,
            "invokeDiscarded");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareIdentifierCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Pop);
    }

    [Theory]
    [InlineData(
        """
        function invokeComputedExpressionKey(box, left, right) {
            return box[left + right]();
        }
        """,
        "invokeComputedExpressionKey",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    [InlineData(
        """
        function invokeDeepComputedCallee(root, key, value) {
            return root.child.branch.leaf[key](value);
        }
        """,
        "invokeDeepComputedCallee",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    [InlineData(
        """
        function readLiteral(box) {
            return { ...box }.value;
        }
        """,
        "readLiteral",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    [InlineData(
        """
        function readDynamic(box) {
            return box[externalKey];
        }
        """,
        "readDynamic",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    [InlineData(
        """
        function readBinaryTarget(a, b) {
            return (a + b).value;
        }
        """,
        "readBinaryTarget",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    [InlineData(
        """
        function readComputedObjectLiteralKey(box) {
            return box[{ value: 1 }];
        }
        """,
        "readComputedObjectLiteralKey",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    [InlineData(
        """
        function readComputedSpreadKey(box, source) {
            return box[{ ...source }];
        }
        """,
        "readComputedSpreadKey",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    [InlineData(
        """
        function logicalWrite(box, value) {
            return box.value ||= value;
        }
        """,
        "logicalWrite",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    [InlineData(
        """
        function logicalAndWrite(box, value) {
            return box.value &&= value;
        }
        """,
        "logicalAndWrite",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    [InlineData(
        """
        function nullishWrite(box, value) {
            return box.value ??= value;
        }
        """,
        "nullishWrite",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    [InlineData(
        """
        var externalValue = 42;
        function dynamicValueWrite(box) {
            return box.value = externalValue;
        }
        """,
        "dynamicValueWrite",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    [InlineData(
        """
        function computedExpressionWrite(box, key, suffix, value) {
            return box[key + suffix] = value;
        }
        """,
        "computedExpressionWrite",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    [InlineData(
        """
        function complexCompoundWrite(box, value) {
            return box.child.value += value;
        }
        """,
        "complexCompoundWrite",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    public void Evaluate_PropertyReadAdjacentFamilies_DeclineWithExplicitCodes(
        string source,
        string functionName,
        int expectedCode)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        var expectedDeclineCode = (UnifiedBytecodeProductionDeclineCode)expectedCode;
        if (expectedDeclineCode == UnifiedBytecodeProductionDeclineCode.None)
        {
            Assert.True(result.IsEligible, $"{result.Code}: {result.Reason}");
            Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
            return;
        }

        Assert.False(result.IsEligible);
        Assert.Equal(expectedDeclineCode, result.Code);
    }

    [Fact]
    public void Evaluate_AssignmentDestructuringPropertyTarget_AcceptsDescriptorOpcode()
    {
        var plan = GetFunctionPlan("""
            function destructureWrite(box, source) {
                ({ value: box.value } = source);
                return box.value;
            }
            """,
            "destructureWrite");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ApplyBindingTarget);
        Assert.NotEmpty(result.Program.BindingTargetConstants);
    }

    [Fact]
    public void Evaluate_SuperPropertyAccess_AcceptsOwnedOpcodes()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                get value() {
                    return 1;
                }
            }

            class Derived extends Base {
                read(name) {
                    return super.value + super[name];
                }
            }
            """,
            "Derived",
            "read");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedSuperProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedSuperProperty);
    }

    [Fact]
    public void Evaluate_NamedSuperCall_AcceptsSuperCallTargetPreparation()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                read(value) {
                    return value;
                }
            }

            class Derived extends Base {
                read(value) {
                    return super.read(value);
                }
            }
            """,
            "Derived",
            "read");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedSuperCallTarget);
        Assert.Contains(result.Program.CallTargetConstants, callTarget =>
            callTarget.Kind == UnifiedBytecodeCallTargetKind.NamedSuperMember);
    }

    [Fact]
    public void Evaluate_ConstructorSuperPropertyCalls_AcceptSuperCallTargetPreparation()
    {
        var plan = GetClassConstructorPlan("""
            class Base {
                read(value) {
                    return this.value + value;
                }
            }

            class Derived extends Base {
                constructor(name, value) {
                    super();
                    this.value = value;
                    this.named = super.read(1);
                    this.computed = super[name](2);
                }
            }
            """,
            "Derived");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedSuperCallTarget);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedSuperCallTarget);
        Assert.Contains(result.Program.CallTargetConstants, callTarget =>
            callTarget.Kind == UnifiedBytecodeCallTargetKind.NamedSuperMember);
        Assert.Contains(result.Program.CallTargetConstants, callTarget =>
            callTarget.Kind == UnifiedBytecodeCallTargetKind.ComputedSuperMember);
    }

    [Fact]
    public void Evaluate_SuperCall_AcceptsSuperConstructInvocationBoundary()
    {
        var plan = GetClassConstructorPlan("""
            class Base {
                constructor(value) {
                    this.value = value;
                }
            }

            class Derived extends Base {
                constructor(value) {
                    super(value);
                }
            }
            """,
            "Derived");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SuperConstructInvocationBoundary);
    }

    [Fact]
    public void Evaluate_SpreadSuperCall_AcceptsSuperConstructInvocationBoundary()
    {
        var plan = GetClassConstructorPlan("""
            class Base {
                constructor(value) {
                    this.value = value;
                }
            }

            class Derived extends Base {
                constructor(values) {
                    super(...values);
                }
            }
            """,
            "Derived");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SuperConstructInvocationBoundary);
        Assert.NotEmpty(result.Program.CallSpreadMasks);
    }

    [Fact]
    public void Evaluate_SuperCallWithSimpleParameterPropertyReadArgument_AcceptsSuperConstructInvocationBoundary()
    {
        var plan = GetClassConstructorPlan("""
            class Base {
                constructor(count) {
                    this.count = count;
                }
            }

            class Derived extends Base {
                constructor(values) {
                    super(values.length);
                }
            }
            """,
            "Derived");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                HasClassConstructorActivation: false));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SuperConstructInvocationBoundary);
    }

    [Fact]
    public void Evaluate_SuperCallWithPropertyReadArgument_AcceptsSuperConstructInvocationBoundary()
    {
        var plan = GetClassConstructorPlan("""
            class Base {
                constructor(count) {
                    this.count = count;
                }
            }

            class Derived extends Base {
                constructor(prefix, ...items) {
                    super(items.length);
                }
            }
            """,
            "Derived");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                HasClassConstructorActivation: false));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SuperConstructInvocationBoundary);
    }

    [Fact]
    public void Evaluate_SuperCallWithComputedPropertyReadArgument_AcceptsSuperConstructInvocationBoundary()
    {
        var plan = GetClassConstructorPlan("""
            class Base {
                constructor(count) {
                    this.count = count;
                }
            }

            class Derived extends Base {
                constructor(prefix, ...items) {
                    super(items["length"]);
                }
            }
            """,
            "Derived");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                HasClassConstructorActivation: false));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SuperConstructInvocationBoundary);
    }

    [Fact]
    public void Evaluate_DefaultDerivedConstructor_AcceptsSuperConstructInvocationBoundary()
    {
        var plan = GetClassConstructorPlan("""
            class Base {
                constructor(value) {
                    this.value = value;
                }
            }

            class Derived extends Base {
            }
            """,
            "Derived");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                HasClassConstructorActivation: false));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SuperConstructInvocationBoundary);
        Assert.NotEmpty(result.Program.CallSpreadMasks);
    }

    [Fact]
    public void Evaluate_PrivateFieldIn_AcceptsAndVmChecksPrivateBrand()
    {
        var plan = GetClassMethodPlan("""
            class Holder {
                #value = 1;
                has(receiver) {
                    return #value in receiver;
                }
            }
            """,
            "Holder",
            "has");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.PrivateFieldIn);

        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var privateNameScope = new PrivateNameScope(engine.RealmState);
        _ = privateNameScope.GetKey("#value");
        using var privateScopeHandle = context.EnterPrivateNameScope(privateNameScope);
        var receiver = new JsObject { RealmState = engine.RealmState };
        receiver.AddPrivateBrand(privateNameScope.BrandToken);
        var slots = new JsValue[Math.Max(result.Program.SlotCount, 1)];
        SetSlot(result.Program, slots, "receiver", JsValue.FromJsObject(receiver));

        var vmResult = UnifiedBytecodeVirtualMachine.Execute(result.Program, slots, context);

        Assert.True(vmResult.AsBoolean());
    }

    [Fact]
    public void Evaluate_PrivateNamedPropertyRead_AcceptsNamedPropertyOpcode()
    {
        var plan = GetClassMethodPlan("""
            class Holder {
                #field = 1;
                read(receiver) {
                    return receiver.#field;
                }
            }
            """,
            "Holder",
            "read");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
    }

    [Fact]
    public void Evaluate_PrivateNamedPropertyWrite_AcceptsNamedPropertyOpcode()
    {
        var plan = GetClassMethodPlan("""
            class Holder {
                #field = 1;
                write(receiver, value) {
                    return receiver.#field = value;
                }
            }
            """,
            "Holder",
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
    }

    [Fact]
    public void Evaluate_PrivateNamedPropertyUpdate_AcceptsNamedPropertyOpcode()
    {
        var plan = GetClassMethodPlan("""
            class Holder {
                #field = 1;
                read(receiver) {
                    return receiver.#field;
                }

                write(receiver, value) {
                    return receiver.#field = value;
                }

                update(receiver) {
                    return receiver.#field++;
                }
            }
            """,
            "Holder",
            "update");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateNamedProperty);
    }

    [Fact]
    public void Evaluate_PrivateNamedCompoundPropertyWrite_AcceptsNamedPropertyOpcodes()
    {
        var plan = GetClassMethodPlan("""
            class Holder {
                #field = 1;
                add(receiver, value) {
                    return receiver.#field += value;
                }
            }
            """,
            "Holder",
            "add");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Equal("#field", Assert.Single(result.Program.StringConstants));
    }

    [Theory]
    [InlineData("&&=", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitFalse)]
    [InlineData("||=", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitTrue)]
    [InlineData("??=", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish)]
    public void Evaluate_PrivateNamedLogicalPropertyWrite_AcceptsNamedPropertyOpcodes(
        string logicalOperator,
        int expectedJumpOpCode)
    {
        var plan = GetClassMethodPlan($$"""
            class Holder {
                #field = 1;
                assign(receiver, value) {
                    return receiver.#field {{logicalOperator}} value;
                }
            }
            """,
            "Holder",
            "assign");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == (UnifiedBytecodeOpCode)expectedJumpOpCode);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Equal("#field", Assert.Single(result.Program.StringConstants));
    }

    [Fact]
    public void Evaluate_PrivateNamedMethodCall_AcceptsCallTargetPreparation()
    {
        var plan = GetClassMethodPlan("""
            class Holder {
                #read(value) {
                    return value + 1;
                }

                call(receiver, value) {
                    return receiver.#read(value);
                }
            }
            """,
            "Holder",
            "call");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        var callTarget = Assert.Single(result.Program.CallTargetConstants);
        Assert.Equal(UnifiedBytecodeCallTargetKind.NamedMember, callTarget.Kind);
        Assert.Equal("#read", result.Program.StringConstants[callTarget.NameConstantIndex]);
    }

    [Fact]
    public void Evaluate_PrivateNamedPropertyDelete_IsRejectedBeforeEligibility()
    {
        var ex = Assert.Throws<ParseException>(() =>
            GetClassMethodPlan("""
                class Holder {
                    #field = 1;
                    remove(receiver) {
                        return delete receiver.#field;
                    }
                }
                """,
                "Holder",
                "remove"));

        Assert.Contains("Private field '#field' cannot be deleted", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        """
        function remove(box) {
            return delete box.child[externalKey];
        }
        """,
        "remove",
        (int)UnifiedBytecodeProductionDeclineCode.None)]
    public void Evaluate_NestedComputedPropertyDeleteDynamicKey_AcceptsOrdinaryDynamicNameOpcode(
        string source,
        string functionName,
        int expectedCode)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, (UnifiedBytecodeProductionDeclineCode)expectedCode);
        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteComputedProperty);
    }

    [Fact]
    public void Evaluate_ArgumentsAccess_AcceptsImplicitArgumentsObjectRead()
    {
        var plan = GetFunctionPlan("""
            function readArguments() {
                return arguments[0];
            }
            """,
            "readArguments");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_ReturnImplicitArgumentsObject_AcceptsDynamicIdentifierRead()
    {
        var plan = GetFunctionPlan("""
            function returnArguments() {
                return arguments;
            }
            """,
            "returnArguments");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_PassImplicitArgumentsObject_AcceptsDynamicIdentifierRead()
    {
        var plan = GetFunctionPlan("""
            function passArguments(reader, value) {
                return reader(arguments);
            }
            """,
            "passArguments");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_LiteralDefaultArgumentsLength_AcceptsImplicitArgumentsObjectPropertyRead()
    {
        var plan = GetFunctionPlan("""
            function readArguments(value = 42) {
                return value + ":" + arguments.length;
            }
            """,
            "readArguments");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true,
                AllowsImplicitArgumentsObjectPropertyReadOperands: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
    }

    [Fact]
    public void Evaluate_LiteralDefaultArgumentsIndexRead_AcceptsImplicitArgumentsObjectPropertyRead()
    {
        var plan = GetFunctionPlan("""
            function readArguments(value = 42) {
                return value + ":" + arguments[0];
            }
            """,
            "readArguments");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true,
                AllowsImplicitArgumentsObjectPropertyReadOperands: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
    }

    [Fact]
    public void Evaluate_ParameterNamedArguments_AcceptsAsActivationSlot()
    {
        var plan = GetFunctionPlan("""
            function readShadow(arguments) {
                let value = arguments;
                return value + 1;
            }
            """,
            "readShadow");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Evaluate_LexicalArgumentsBinding_AcceptsAsActivationSlot()
    {
        var plan = GetFunctionPlan("""
            function readShadow() {
                let arguments = 41;
                return arguments + 1;
            }
            """,
            "readShadow");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Evaluate_TypeOfParameterNamedArguments_AcceptsAsActivationSlot()
    {
        var plan = GetFunctionPlan("""
            function readShadow(arguments) {
                return typeof arguments;
            }
            """,
            "readShadow");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Evaluate_TypeOfImplicitArgumentsObject_AcceptsObjectType()
    {
        var plan = GetFunctionPlan("""
            function readArguments() {
                return typeof arguments;
            }
            """,
            "readArguments");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode is UnifiedBytecodeOpCode.LoadLiteral or UnifiedBytecodeOpCode.TypeOfDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_UpdateParameterNamedArguments_AcceptsAsActivationSlot()
    {
        var plan = GetFunctionPlan("""
            function bump(arguments) {
                arguments++;
                return arguments;
            }
            """,
            "bump");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Evaluate_UpdateImplicitArgumentsObject_AcceptsDynamicIdentifierUpdate()
    {
        var plan = GetFunctionPlan("""
            function bump() {
                arguments++;
                return arguments;
            }
            """,
            "bump");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_DeleteImplicitArgumentsObject_AcceptsDynamicIdentifierDelete()
    {
        var plan = GetFunctionPlan("""
            function removeArguments() {
                return delete arguments;
            }
            """,
            "removeArguments");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_CallParameterNamedArguments_AcceptsAsActivationSlot()
    {
        var plan = GetFunctionPlan("""
            function callShadow(arguments, value) {
                return arguments(value);
            }
            """,
            "callShadow");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Evaluate_CallImplicitArgumentsObject_AcceptsDynamicIdentifierCallTarget()
    {
        var plan = GetFunctionPlan("""
            function callArguments() {
                return arguments();
            }
            """,
            "callArguments");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget);
    }

    [Fact]
    public void Evaluate_AssignImplicitArgumentsObject_AcceptsActivationSlotWrite()
    {
        var plan = GetFunctionPlan("""
            function assignArguments() {
                arguments;
                arguments = 1;
                return arguments;
            }
            """,
            "assignArguments");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_LiteralDefaultArgumentsUpdate_AcceptsDynamicIdentifierUpdate()
    {
        var plan = GetFunctionPlan("""
            function bump(value = 42) {
                arguments++;
                return value + ":" + arguments;
            }
            """,
            "bump");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_LiteralDefaultArgumentsDelete_AcceptsDynamicIdentifierDelete()
    {
        var plan = GetFunctionPlan("""
            function removeArguments(value = 42) {
                return value + ":" + (delete arguments);
            }
            """,
            "removeArguments");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_LiteralDefaultArgumentsCall_AcceptsDynamicIdentifierCallTarget()
    {
        var plan = GetFunctionPlan("""
            function callArguments(value = 42) {
                return arguments();
            }
            """,
            "callArguments");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget);
    }

    [Fact]
    public void Evaluate_LiteralDefaultArgumentsAssignment_AcceptsDynamicIdentifierStore()
    {
        var plan = GetFunctionPlan("""
            function assignArguments(value = 42) {
                arguments;
                arguments = 7;
                return value + ":" + arguments;
            }
            """,
            "assignArguments");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        // Dynamic identifier assignment resolves the LHS reference before the RHS
        // (§13.15.2), so it lowers to ResolveDynamicIdentifierReference + StoreDynamicIdentifierReference.
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.StoreDynamicIdentifierReference);
    }

    [Fact]
    public void Evaluate_DynamicIdentifierLookup_AcceptsOrdinaryDynamicNameOpcode()
    {
        var plan = GetFunctionPlan("""
            function readExternal() {
                return externalValue;
            }
            """,
            "readExternal");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_DynamicIdentifierAssignmentExpression_AcceptsEnvironmentReferenceOpcodes()
    {
        var plan = GetFunctionPlan("""
            function writeExternal(value) {
                return externalValue = value;
            }
            """,
            "writeExternal");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.StoreDynamicIdentifierReference);
    }

    [Fact]
    public void Evaluate_DynamicIdentifierUpdateExpression_AcceptsEnvironmentOpcode()
    {
        var plan = GetFunctionPlan("""
            function updateExternal() {
                return externalValue++;
            }
            """,
            "updateExternal");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_DynamicIdentifierDeleteExpression_AcceptsEnvironmentOpcode()
    {
        var plan = GetFunctionPlan("""
            function removeExternal() {
                return delete externalValue;
            }
            """,
            "removeExternal");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.DeleteDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_OrdinaryDynamicIdentifierOperations_AcceptsEnvironmentOpcodes()
    {
        var plan = GetFunctionPlan("""
            function useGlobals(delta) {
                externalValue = externalValue + delta;
                ++globalCount;
                var missingType = typeof missing;
                var deleteResult = delete removable;
                return externalValue + globalCount;
            }
            """,
            "useGlobals");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.StoreDynamicIdentifierReference);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.TypeOfDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeleteDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_ActivationSlotIncrementInstruction_AcceptsUpdateSlotOpcode()
    {
        var plan = GetFunctionPlan("""
            function bump(x) {
                x++;
                return x;
            }
            """,
            "bump");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateSlot);
        Assert.DoesNotContain(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateDynamicIdentifier);

        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var slots = new JsValue[Math.Max(result.Program.SlotCount, 1)];
        SetSlot(result.Program, slots, "x", JsValue.FromDouble(41));

        var vmResult = UnifiedBytecodeVirtualMachine.Execute(result.Program, slots, context);

        Assert.Equal(42d, vmResult.AsDouble());
    }

    [Theory]
    [InlineData("function post(x) { return x++; }", "post", 4d)]
    [InlineData("function pre(x) { return ++x; }", "pre", 5d)]
    public void Evaluate_ActivationSlotUpdateExpression_AcceptsUpdateSlotOpcode(
        string source,
        string functionName,
        double expectedResult)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateSlot);
        Assert.DoesNotContain(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateDynamicIdentifier);

        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var slots = new JsValue[Math.Max(result.Program.SlotCount, 1)];
        SetSlot(result.Program, slots, "x", JsValue.FromDouble(4));

        var vmResult = UnifiedBytecodeVirtualMachine.Execute(result.Program, slots, context);

        Assert.Equal(expectedResult, vmResult.AsDouble());
    }

    [Fact]
    public void Evaluate_RegexLiteralReturn_AcceptsLoadRegexLiteralOpcode()
    {
        var plan = GetFunctionPlan("""
            function makeRegex() {
                return /hello/gi;
            }
            """,
            "makeRegex");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadRegexLiteral);

        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var slots = new JsValue[Math.Max(result.Program.SlotCount, 1)];

        var vmResult = UnifiedBytecodeVirtualMachine.Execute(result.Program, slots, context);

        var regexObject = Assert.IsType<JsObject>(vmResult.ObjectValue, exactMatch: false);
        Assert.True(regexObject.TryGetProperty("__regex__", out var regexMarker));
        var regex = Assert.IsType<JsRegExp>(regexMarker.ObjectValue);
        Assert.Equal("hello", regex.Pattern);
        Assert.Equal("gi", regex.Flags);
        Assert.True(regex.Global);
        Assert.True(regex.IgnoreCase);
    }

    [Fact]
    public void Evaluate_ThrowReferenceErrorExpression_AcceptsAndVmThrowsReferenceError()
    {
        var message = "Unsupported reference to 'super'";
        var expressionProgram = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.ThrowReferenceError(0)),
            stringConstants: ImmutableArray.Create(message));
        var seedPlan = GetFunctionPlan("function thrower() { return 0; }", "thrower");
        var plan = seedPlan with
        {
            Instructions = ImmutableArray.Create<ExecutionInstruction>(new ReturnInstruction(-1, expressionProgram)),
            EntryPoint = 0
        };

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ThrowReferenceError);

        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var slots = new JsValue[Math.Max(result.Program.SlotCount, 1)];

        _ = UnifiedBytecodeVirtualMachine.Execute(result.Program, slots, context);

        Assert.True(context.IsThrow);
        var error = Assert.IsType<JsObject>(context.FlowValue.ObjectValue, exactMatch: false);
        Assert.True(error.TryGetProperty("name", out var name));
        Assert.Equal("ReferenceError", name.AsString());
        Assert.True(error.TryGetProperty("message", out var actualMessage));
        Assert.Equal(message, actualMessage.AsString());
    }

    [Fact]
    public void Evaluate_JumpIfShortCircuitedExpression_AcceptsAndVmSkipsShortCircuitedBranch()
    {
        var expressionProgram = new ExpressionProgram(
            ImmutableArray.Create(
                PackedExpressionOp.LoadLiteralConstant(0),
                PackedExpressionOp.GetNamedProperty(0, IsOptional: true),
                PackedExpressionOp.JumpIfShortCircuited(4),
                PackedExpressionOp.ThrowReferenceError(1)),
            literalConstants: ImmutableArray.Create(JsValue.Null),
            stringConstants: ImmutableArray.Create("missing", "short-circuit jump was not taken"));
        var seedPlan = GetFunctionPlan("function optional() { return 0; }", "optional");
        var plan = seedPlan with
        {
            Instructions = ImmutableArray.Create<ExecutionInstruction>(new ReturnInstruction(-1, expressionProgram)),
            EntryPoint = 0
        };

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuited);
        Assert.True(result.Program.RequiresShortCircuitStackFlags);

        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var slots = new JsValue[Math.Max(result.Program.SlotCount, 1)];

        var vmResult = UnifiedBytecodeVirtualMachine.Execute(result.Program, slots, context);

        Assert.False(context.ShouldStopEvaluation);
        Assert.True(vmResult.IsUndefined);
    }

    [Fact]
    public void Evaluate_JumpIfShortCircuitedExpression_DoesNotTreatOrdinaryUndefinedAsShortCircuited()
    {
        var expressionProgram = new ExpressionProgram(
            ImmutableArray.Create(
                PackedExpressionOp.LoadLiteralConstant(0),
                PackedExpressionOp.JumpIfShortCircuited(3),
                PackedExpressionOp.LoadLiteralConstant(1)),
            literalConstants: ImmutableArray.Create(JsValue.Undefined, JsValue.FromDouble(42)));
        var seedPlan = GetFunctionPlan("function ordinaryUndefined() { return 0; }", "ordinaryUndefined");
        var plan = seedPlan with
        {
            Instructions = ImmutableArray.Create<ExecutionInstruction>(new ReturnInstruction(-1, expressionProgram)),
            EntryPoint = 0
        };

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuited);
        Assert.True(result.Program.RequiresShortCircuitStackFlags);

        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var slots = new JsValue[Math.Max(result.Program.SlotCount, 1)];

        var vmResult = UnifiedBytecodeVirtualMachine.Execute(result.Program, slots, context);

        Assert.False(context.ShouldStopEvaluation);
        Assert.Equal(42d, vmResult.AsDouble());
    }

    [Fact]
    public void Evaluate_LoadImportMetaExpression_AcceptsAndVmReadsCallingEnvironmentBinding()
    {
        var expressionProgram = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadImportMeta));
        var seedPlan = GetFunctionPlan("function meta() { return 0; }", "meta");
        var plan = seedPlan with
        {
            Instructions = ImmutableArray.Create<ExecutionInstruction>(new ReturnInstruction(-1, expressionProgram)),
            EntryPoint = 0
        };

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadImportMeta);

        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var slots = new JsValue[Math.Max(result.Program.SlotCount, 1)];
        var moduleEnvironment = JsEnvironment.CreateInstance(engine.GlobalEnvironment, description: "module");
        var importMeta = new JsObject { RealmState = engine.RealmState };
        moduleEnvironment.DefineJsValue(
            Symbol.ImportMeta,
            JsValue.FromJsObject(importMeta),
            isConst: true,
            isLexicalBinding: true);

        var vmResult = UnifiedBytecodeVirtualMachine.Execute(result.Program, slots, context, moduleEnvironment);

        Assert.False(context.ShouldStopEvaluation);
        Assert.Same(importMeta, vmResult.ObjectValue);
    }

    [Fact]
    public void Evaluate_LoadTemplateObjectExpression_AcceptsAndVmCachesTemplateObject()
    {
        var descriptor = new TaggedTemplateDescriptor(
            ImmutableArray.Create(JsValue.FromString("x")),
            ImmutableArray.Create(JsValue.FromString("x")));
        var expressionProgram = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadTemplateObject(0)),
            objectConstants: ImmutableArray.Create<object>(descriptor));
        var seedPlan = GetFunctionPlan("function template() { return 0; }", "template");
        var plan = seedPlan with
        {
            Instructions = ImmutableArray.Create<ExecutionInstruction>(new ReturnInstruction(-1, expressionProgram)),
            EntryPoint = 0
        };

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadTemplateObject);

        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        var slots = new JsValue[Math.Max(result.Program.SlotCount, 1)];

        var firstResult = UnifiedBytecodeVirtualMachine.Execute(result.Program, slots, context);
        var secondResult = UnifiedBytecodeVirtualMachine.Execute(result.Program, slots, context);

        var templateObject = Assert.IsType<JsArray>(firstResult.ObjectValue);
        Assert.Same(templateObject, secondResult.ObjectValue);
        Assert.Equal("x", templateObject.Items[0].AsString());
        Assert.True(templateObject.TryGetProperty("raw", out var rawValue));
        var rawArray = Assert.IsType<JsArray>(rawValue.ObjectValue);
        Assert.Equal("x", rawArray.Items[0].AsString());
    }

    [Fact]
    public void Evaluate_OrdinaryDynamicAssignmentReference_AcceptsEnvironmentReferenceOpcodes()
    {
        var plan = GetFunctionPlan("""
            function writeGlobal(delta) {
                return externalValue += delta;
            }
            """,
            "writeGlobal");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifierReference);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.StoreDynamicIdentifierReference);
    }

    [Fact]
    public void Evaluate_LabeledLoop_Accepts()
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

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_LabeledBreakOutOfForOf_Accepts()
    {
        // Labeled break out of a for-of where the label is on the for-of itself: the break target is
        // that loop's own exit, so the existing single-level driver cleanup closes the iterator.
        var plan = GetFunctionPlan("""
            function labeled(source) {
                outer: for (var value of source) {
                    break outer;
                }

                return 0;
            }
            """,
            "labeled");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Break);
    }

    [Fact]
    public void Evaluate_LabeledContinueInLoop_Accepts()
    {
        var plan = GetFunctionPlan("""
            function labeled(n) {
                var total = 0;
                outer: while (n > 0) {
                    n = n - 1;
                    continue outer;
                    total = total + 1;
                }

                return total;
            }
            """,
            "labeled");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Continue);
    }

    [Fact]
    public void Evaluate_LabeledContinueCrossingDriverLoop_AcceptsWithDriverCleanupTopology()
    {
        // A labeled continue that re-enters an outer loop from inside an enclosing for-of driver
        // loop is eligible now that cleanup closes the crossed inner driver before jumping to the
        // outer continue target.
        var plan = GetFunctionPlan("""
            function labeled(outer, inner) {
                outerLabel: for (var x of outer) {
                    for (var y of inner) {
                        continue outerLabel;
                    }
                }

                return 0;
            }
            """,
            "labeled");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.IteratorMoveNext);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Continue);
        Assert.True(result.Program.DriverDescriptors.Count(static descriptor => descriptor.BreakTarget >= 0) >= 2);
    }

    [Fact]
    public void Evaluate_LabeledBreakCrossingDriverLoop_AcceptsWithDriverCleanupTopology()
    {
        // A labeled break that exits an enclosing for-of driver loop it is not directly targeting
        // (here: break outerLabel from inside the inner for-of) is eligible now that cleanup closes
        // all crossed active drivers in innermost-first order.
        var plan = GetFunctionPlan("""
            function labeled(outer, inner) {
                outerLabel: for (var x of outer) {
                    for (var y of inner) {
                        break outerLabel;
                    }

                    return -1;
                }

                return 0;
            }
            """,
            "labeled");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Break);
        Assert.True(result.Program.DriverDescriptors.Count(static descriptor => descriptor.BreakTarget >= 0) >= 2);
    }

    [Fact]
    public void Evaluate_LabeledContinueCrossingForInDriverLoop_Accepts()
    {
        var plan = GetFunctionPlan("""
            function labeled(outer, inner) {
                var total = 0;
                outerLabel: for (var x in outer) {
                    for (var y in inner) {
                        continue outerLabel;
                    }

                    total = total + 1;
                }

                return total;
            }
            """,
            "labeled");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ForInMoveNext);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Continue);
    }

    [Fact]
    public void Evaluate_BreakControlFlow_Accepts()
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

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Break);
    }

    [Fact]
    public void Evaluate_ContinueControlFlow_Accepts()
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

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Continue);
    }

    [Fact]
    public void Evaluate_BreakThroughFinallyControlFlow_Accepts()
    {
        var plan = GetFunctionPlan("""
            function breakFinally(n) {
                var marker = 0;
                while (n > 0) {
                    try {
                        break;
                    } finally {
                        marker = marker + 10;
                    }
                }

                return marker;
            }
            """,
            "breakFinally");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Break);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.EndFinally);
    }

    [Fact]
    public void Evaluate_ContinueThroughFinallyControlFlow_Accepts()
    {
        var plan = GetFunctionPlan("""
            function continueFinally(n) {
                var i = 0;
                var marker = 0;
                while (i < n) {
                    i = i + 1;
                    try {
                        continue;
                    } finally {
                        marker = marker + 10;
                    }
                }

                return marker + i;
            }
            """,
            "continueFinally");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Continue);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.EndFinally);
    }

    [Fact]
    public void Evaluate_ForLoopContinueTarget_Accepts()
    {
        var plan = GetFunctionPlan("""
            function continueFor(n) {
                var total = 0;
                for (; n > 0; n = n - 1) {
                    total = total + n;
                    continue;
                    total = 1000;
                }

                return total;
            }
            """,
            "continueFor");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Jump);
    }

    [Fact]
    public void Evaluate_DoWhileLoop_Accepts()
    {
        var plan = GetFunctionPlan("""
            function countDo(n) {
                var count = 0;
                do {
                    count = count + 1;
                    n = n - 1;
                } while (n > 0);

                return count;
            }
            """,
            "countDo");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
    }

    [Fact]
    public void Evaluate_ForInPlan_AcceptsDriverStateOpcodes()
    {
        var plan = GetFunctionPlan("""
            function listKeys(obj) {
                var sum = 0;
                for (var key in obj) {
                    sum = sum + 1;
                }

                return sum;
            }
            """,
            "listKeys");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ForInInit);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ForInMoveNext);
        Assert.NotEmpty(result.Program.DriverDescriptors);
    }

    [Fact]
    public void Evaluate_ArrayDestructuringPlan_AcceptsDriverStateOpcodes()
    {
        var plan = GetFunctionPlan("""
            function readFirst(values) {
                var [first] = values;
                return first;
            }
            """,
            "readFirst");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringInit);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringElement);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringClose);
        Assert.NotEmpty(result.Program.DriverDescriptors);
    }

    [Theory]
    [InlineData(
        """
        function readDefault(values) {
            var [first = 1] = values;
            return first;
        }
        """,
        "readDefault")]
    [InlineData(
        """
        function readComputed(source, key) {
            var { [key]: value } = source;
            return value;
        }
        """,
        "readComputed")]
    public void Evaluate_DeclarationDestructuringDescriptorShapes_AcceptDescriptorOpcode(
        string source,
        string functionName)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ApplyDeclarationBindingTarget);
        Assert.NotEmpty(result.Program.BindingTargetConstants);
    }

    [Fact]
    public void Evaluate_ObjectDestructuringPlan_AcceptsDriverStateOpcodes()
    {
        var plan = GetFunctionPlan("""
            function readAb(source) {
                var { a, b } = source;
                return a + b;
            }
            """,
            "readAb");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ObjectDestructuringInit);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ObjectDestructuringProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ObjectDestructuringClose);
        Assert.NotEmpty(result.Program.DriverDescriptors);
    }

    [Fact]
    public void Evaluate_ObjectDestructuringRestPlan_AcceptsRestDriverOpcode()
    {
        var plan = GetFunctionPlan("""
            function readRest(source) {
                var { a, ...rest } = source;
                return rest;
            }
            """,
            "readRest");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ObjectDestructuringRest);
    }

    [Fact]
    public void Evaluate_BindingVariableDeclaration_StaticPattern_AcceptsDescriptorOpcode()
    {
        var plan = GetFunctionPlan("""
            function destructure(source) {
                {
                    let { nested: { value } } = source;
                    return value;
                }
            }
            """,
            "destructure");

        Assert.Contains(plan.Instructions, static instruction => instruction is BindingVariableDeclarationInstruction);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ApplyDeclarationBindingTarget);
        Assert.NotEmpty(result.Program.BindingTargetConstants);
    }

    [Fact]
    public void Evaluate_AssignmentDestructuringApplyBindingTarget_AcceptsDescriptorOpcode()
    {
        var plan = GetFunctionPlan("""
            function assignComputedDefault(source, key) {
                var value = 0;
                ({ [key]: value = 5 } = source);
                return value;
            }
            """,
            "assignComputedDefault");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ApplyBindingTarget);
        Assert.NotEmpty(result.Program.BindingTargetConstants);
    }

    [Theory]
    [InlineData(
        """
        function readObjectDefault(source) {
            var { a = 1 } = source;
            return a;
        }
        """,
        "readObjectDefault")]
    [InlineData(
        """
        function readObjectComputed(source, key) {
            var { [key]: value } = source;
            return value;
        }
        """,
        "readObjectComputed")]
    public void Evaluate_ObjectDeclarationDestructuringDescriptorShapes_AcceptDescriptorOpcode(
        string source,
        string functionName)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ApplyDeclarationBindingTarget);
        Assert.NotEmpty(result.Program.BindingTargetConstants);
    }

    [Fact]
    public void Evaluate_ForOfPlan_AcceptsIteratorDriverOpcodes()
    {
        var plan = GetFunctionPlan("""
            function sumValues(values) {
                var sum = 0;
                for (var value of values) {
                    sum = sum + value;
                }

                return sum;
            }
            """,
            "sumValues");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.IteratorInit);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.IteratorMoveNext);
    }

    [Fact]
    public void Evaluate_ForInTdzHead_IsAdmittedWithTdzHeadInit()
    {
        // Slice A (#2678): a lexical for-in head over a flat-slot source is now admitted.
        // Previously declined with ForInDriverStateDependency ("TDZ head").
        var plan = GetFunctionPlan("""
            function collect(obj) {
                var keys = "";
                for (const key in obj) {
                    keys = keys + key;
                }

                return keys;
            }
            """,
            "collect");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.TdzHeadInit);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ForInInit);
    }

    [Fact]
    public void Evaluate_ForOfTdzHead_IsAdmittedWithTdzHeadInit()
    {
        // Slice A (#2678): a lexical for-of head over a flat-slot source is now admitted.
        var plan = GetFunctionPlan("""
            function sum(values) {
                var total = 0;
                for (const value of values) {
                    total = total + value;
                }

                return total;
            }
            """,
            "sum");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.TdzHeadInit);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.IteratorInit);
    }

    // Payload proof (#2678/B41): sync TDZ heads, awaited sources, and async iterator
    // drivers are admitted, while invalid source payload shapes still decline before compilation.

    [Fact]
    public void IsSupportedIteratorInit_SyncIterableSource_Accepts()
    {
        var instruction = new IteratorInitInstruction(
            IteratorDriverKind.Sync,
            Symbol.Synthetic("__iter_state"),
            IteratorSlotIndex: 0,
            Next: -1,
            IterableProgram: ExpressionProgram.Empty);

        Assert.True(UnifiedBytecodeProductionEligibility.IsSupportedIteratorInit(instruction, out var reason), reason);
    }

    [Fact]
    public void IsSupportedIteratorInit_AsyncKind_Accepts()
    {
        var instruction = new IteratorInitInstruction(
            IteratorDriverKind.Await,
            Symbol.Synthetic("__iter_state"),
            IteratorSlotIndex: 0,
            Next: -1,
            IterableProgram: ExpressionProgram.Empty);

        Assert.True(UnifiedBytecodeProductionEligibility.IsSupportedIteratorInit(instruction, out var reason), reason);
    }

    [Fact]
    public void IsSupportedIteratorInit_AsyncKindWithAwaitedSource_Accepts()
    {
        var instruction = new IteratorInitInstruction(
            IteratorDriverKind.Await,
            Symbol.Synthetic("__iter_state"),
            IteratorSlotIndex: 0,
            Next: -1,
            IterableProgram: null,
            AwaitedProgram: ExpressionProgram.Empty);

        Assert.True(UnifiedBytecodeProductionEligibility.IsSupportedIteratorInit(instruction, out var reason), reason);
    }

    [Fact]
    public void IsSupportedIteratorInit_MissingSourcePayload_Declines()
    {
        var instruction = new IteratorInitInstruction(
            IteratorDriverKind.Sync,
            Symbol.Synthetic("__iter_state"),
            IteratorSlotIndex: 0,
            Next: -1);

        Assert.False(UnifiedBytecodeProductionEligibility.IsSupportedIteratorInit(instruction, out var reason));
        Assert.Contains("exactly one expression bytecode payload", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void IsSupportedIteratorInit_DualSourcePayload_Declines()
    {
        var instruction = new IteratorInitInstruction(
            IteratorDriverKind.Sync,
            Symbol.Synthetic("__iter_state"),
            IteratorSlotIndex: 0,
            Next: -1,
            IterableProgram: ExpressionProgram.Empty,
            AwaitedProgram: ExpressionProgram.Empty);

        Assert.False(UnifiedBytecodeProductionEligibility.IsSupportedIteratorInit(instruction, out var reason));
        Assert.Contains("exactly one expression bytecode payload", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void IsSupportedIteratorInit_AwaitedSource_Accepts()
    {
        var instruction = new IteratorInitInstruction(
            IteratorDriverKind.Sync,
            Symbol.Synthetic("__iter_state"),
            IteratorSlotIndex: 0,
            Next: -1,
            IterableProgram: null,
            AwaitedProgram: ExpressionProgram.Empty);

        Assert.True(UnifiedBytecodeProductionEligibility.IsSupportedIteratorInit(instruction, out var reason), reason);
    }

    [Fact]
    public void IsSupportedForInInit_AwaitedSource_Accepts()
    {
        var instruction = new ForInInitInstruction(
            Symbol.Synthetic("__forIn_state"),
            StateSlotIndex: 0,
            Symbol.Synthetic("__forIn_value"),
            ValueSlotIndex: 1,
            Next: -1,
            ObjectProgram: null,
            AwaitedProgram: ExpressionProgram.Empty);

        Assert.True(UnifiedBytecodeProductionEligibility.IsSupportedForInInit(instruction, out var reason), reason);
    }

    [Fact]
    public void IsSupportedForInInit_DualSourcePayload_Declines()
    {
        var instruction = new ForInInitInstruction(
            Symbol.Synthetic("__forIn_state"),
            StateSlotIndex: 0,
            Symbol.Synthetic("__forIn_value"),
            ValueSlotIndex: 1,
            Next: -1,
            ObjectProgram: ExpressionProgram.Empty,
            AwaitedProgram: ExpressionProgram.Empty);

        Assert.False(UnifiedBytecodeProductionEligibility.IsSupportedForInInit(instruction, out var reason));
        Assert.Contains("exactly one expression bytecode payload", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_AwaitedForInSource_AdmitsAwaitValueAndForInDriver()
    {
        var plan = GetFunctionPlan("""
            async function collect(sourcePromise) {
                var last = "";
                for (var key in await sourcePromise) {
                    last = key;
                }

                return last;
            }
            """,
            "collect");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.AwaitValue);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ForInInit);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ForInMoveNext);
    }

    [Fact]
    public void EvaluateResumable_AwaitedForOfSource_AdmitsAwaitValueAndIteratorDriver()
    {
        var plan = GetFunctionPlan("""
            async function collect(sourcePromise) {
                var last = 0;
                for (var value of await sourcePromise) {
                    last = value;
                }

                return last;
            }
            """,
            "collect");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.AwaitValue);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.IteratorInit);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.IteratorMoveNext);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.IteratorClose);
    }

    [Fact]
    public void EvaluateResumable_ForAwaitOfDriver_AdmitsAsyncIteratorDriver()
    {
        var plan = GetFunctionPlan("""
            async function collect(values) {
                var last = 0;
                for await (var value of values) {
                    last = value;
                }

                return last;
            }
            """,
            "collect");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.IteratorInit);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.IteratorMoveNext);
        Assert.Contains(result.Program.DriverDescriptors, descriptor =>
            descriptor.IteratorKind == IteratorDriverKind.Await);
    }

    [Fact]
    public void EvaluateResumable_ForInDriverAcrossYield_AdmitsForInDriver()
    {
        var plan = GetFunctionPlan("""
            function* keys(obj) {
                for (var key in obj) {
                    yield key;
                }
            }
            """,
            "keys");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ForInInit);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ForInMoveNext);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Yield);
    }

    [Fact]
    public void EvaluateResumable_ForOfDriverAcrossYield_AdmitsIteratorDriver()
    {
        var plan = GetFunctionPlan("""
            function* values(items) {
                for (var value of items) {
                    yield value;
                }
            }
            """,
            "values");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.IteratorInit);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.IteratorMoveNext);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.IteratorClose);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Yield);
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
    public void Evaluate_StrictEqualityOperator_AcceptsProductionSubset()
    {
        var plan = GetFunctionPlan("""
            function strictEqual(a, b) {
                return a === b;
            }
            """,
            "strictEqual");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: (int)BinaryOperator.StrictEqual });
    }

    [Fact]
    public void Evaluate_PrimitiveOperatorLane_AcceptsOwnedOpcodes()
    {
        var plan = GetFunctionPlan("""
            function primitiveLane(value) {
                var text = `${value}`;
                value;
                return typeof value + ":" + (+value) + ":" + (-value) + ":" + (!value) + ":" + (~value) + ":" + (void value) + ":" + text;
            }
            """,
            "primitiveLane");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.TypeOfIdentifier);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryPlus);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryMinus);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryLogicalNot);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryBitwiseNot);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryVoid);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.ToString);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.Pop);
    }

    [Fact]
    public void Evaluate_TypeOfNonIdentifier_AcceptsTypeOfOpcode()
    {
        var plan = GetFunctionPlan("""
            function kind(value) {
                return typeof (value + 1);
            }
            """,
            "kind");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.TypeOf);
    }

    [Fact]
    public void Evaluate_TypeOfUnresolvedIdentifier_AcceptsOrdinaryDynamicNameOpcode()
    {
        var plan = GetFunctionPlan("""
            function kind() {
                return typeof missing;
            }
            """,
            "kind");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.TypeOfDynamicIdentifier);
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
            { "power", "**", (int)BinaryOperator.Power },
            { "equal", "==", (int)BinaryOperator.Equal },
            { "notEqual", "!=", (int)BinaryOperator.NotEqual },
            { "strictEqual", "===", (int)BinaryOperator.StrictEqual },
            { "strictNotEqual", "!==", (int)BinaryOperator.StrictNotEqual },
            { "lessThan", "<", (int)BinaryOperator.LessThan },
            { "lessThanOrEqual", "<=", (int)BinaryOperator.LessThanOrEqual },
            { "greaterThan", ">", (int)BinaryOperator.GreaterThan },
            { "greaterThanOrEqual", ">=", (int)BinaryOperator.GreaterThanOrEqual },
            { "bitwiseAnd", "&", (int)BinaryOperator.BitwiseAnd },
            { "bitwiseOr", "|", (int)BinaryOperator.BitwiseOr },
            { "bitwiseXor", "^", (int)BinaryOperator.BitwiseXor },
            { "leftShift", "<<", (int)BinaryOperator.LeftShift },
            { "rightShift", ">>", (int)BinaryOperator.RightShift },
            { "unsignedRightShift", ">>>", (int)BinaryOperator.UnsignedRightShift },
            { "inOp", "in", (int)BinaryOperator.In },
            { "instanceOf", "instanceof", (int)BinaryOperator.InstanceOf }
        };

    public static TheoryData<string, string> CompoundNamedPropertyWriteOperators =>
        new()
        {
            { "addAssign", "+=" },
            { "subtractAssign", "-=" },
            { "multiplyAssign", "*=" },
            { "divideAssign", "/=" },
            { "moduloAssign", "%=" },
            { "powerAssign", "**=" },
            { "bitwiseAndAssign", "&=" },
            { "bitwiseOrAssign", "|=" },
            { "bitwiseXorAssign", "^=" },
            { "leftShiftAssign", "<<=" },
            { "rightShiftAssign", ">>=" },
            { "unsignedRightShiftAssign", ">>>=" }
        };

    public static TheoryData<string, string, int[], int[]>
        AcceptedPropertyWriteAndUpdatePrograms =>
        new()
        {
            {
                """
                function write(box, value) {
                    return box.value = value;
                }
                """,
                "write",
                [(int)UnifiedBytecodeOpCode.SetNamedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.SetNamedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            },
            {
                """
                function write(box, key, value) {
                    return box[key] = value;
                }
                """,
                "write",
                [(int)UnifiedBytecodeOpCode.SetComputedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.RequireObjectCoercible,
                    (int)UnifiedBytecodeOpCode.ResolvePropertyKey,
                    (int)UnifiedBytecodeOpCode.SetComputedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            },
            {
                """
                function write(box, value) {
                    return box.value += value;
                }
                """,
                "write",
                [(int)UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet, (int)UnifiedBytecodeOpCode.SetNamedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet,
                    (int)UnifiedBytecodeOpCode.Binary,
                    (int)UnifiedBytecodeOpCode.SetNamedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            },
            {
                """
                function write(box, key, value) {
                    return box[key] += value;
                }
                """,
                "write",
                [(int)UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet, (int)UnifiedBytecodeOpCode.SetComputedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.RequireObjectCoercible,
                    (int)UnifiedBytecodeOpCode.ResolvePropertyKey,
                    (int)UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet,
                    (int)UnifiedBytecodeOpCode.Binary,
                    (int)UnifiedBytecodeOpCode.SetComputedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            },
            {
                """
                function update(box) {
                    return ++box.value;
                }
                """,
                "update",
                [(int)UnifiedBytecodeOpCode.UpdateNamedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.UpdateNamedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            },
            {
                """
                function update(box) {
                    return box.value++;
                }
                """,
                "update",
                [(int)UnifiedBytecodeOpCode.UpdateNamedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.UpdateNamedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            },
            {
                """
                function update(box) {
                    return --box.value;
                }
                """,
                "update",
                [(int)UnifiedBytecodeOpCode.UpdateNamedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.UpdateNamedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            },
            {
                """
                function update(box) {
                    return box.value--;
                }
                """,
                "update",
                [(int)UnifiedBytecodeOpCode.UpdateNamedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.UpdateNamedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            },
            {
                """
                function update(box, key) {
                    return ++box[key];
                }
                """,
                "update",
                [(int)UnifiedBytecodeOpCode.UpdateComputedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.RequireObjectCoercible,
                    (int)UnifiedBytecodeOpCode.ResolvePropertyKey,
                    (int)UnifiedBytecodeOpCode.UpdateComputedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            },
            {
                """
                function update(box, key) {
                    return box[key]++;
                }
                """,
                "update",
                [(int)UnifiedBytecodeOpCode.UpdateComputedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.RequireObjectCoercible,
                    (int)UnifiedBytecodeOpCode.ResolvePropertyKey,
                    (int)UnifiedBytecodeOpCode.UpdateComputedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            },
            {
                """
                function update(box, key) {
                    return --box[key];
                }
                """,
                "update",
                [(int)UnifiedBytecodeOpCode.UpdateComputedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.RequireObjectCoercible,
                    (int)UnifiedBytecodeOpCode.ResolvePropertyKey,
                    (int)UnifiedBytecodeOpCode.UpdateComputedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            },
            {
                """
                function update(box, key) {
                    return box[key]--;
                }
                """,
                "update",
                [(int)UnifiedBytecodeOpCode.UpdateComputedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.RequireObjectCoercible,
                    (int)UnifiedBytecodeOpCode.ResolvePropertyKey,
                    (int)UnifiedBytecodeOpCode.UpdateComputedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            },
            {
                """
                function writeNested(box, value) {
                    return box.child.value = value;
                }
                """,
                "writeNested",
                [(int)UnifiedBytecodeOpCode.GetNamedProperty, (int)UnifiedBytecodeOpCode.SetNamedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.GetNamedProperty,
                    (int)UnifiedBytecodeOpCode.SetNamedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            },
            {
                """
                function updateNested(box) {
                    return box.child.value++;
                }
                """,
                "updateNested",
                [(int)UnifiedBytecodeOpCode.GetNamedProperty, (int)UnifiedBytecodeOpCode.UpdateNamedProperty],
                [
                    (int)UnifiedBytecodeOpCode.LoadSlot,
                    (int)UnifiedBytecodeOpCode.LoadLiteral,
                    (int)UnifiedBytecodeOpCode.StoreSlot,
                    (int)UnifiedBytecodeOpCode.GetNamedProperty,
                    (int)UnifiedBytecodeOpCode.UpdateNamedProperty,
                    (int)UnifiedBytecodeOpCode.Return
                ]
            }
        };

    private static void AssertAllInstructionsUseOwnedOpcodes(
        UnifiedBytecodeProgram program,
        IReadOnlyCollection<int> allowedOpcodes)
    {
        Assert.All(
            program.Instructions,
            instruction => Assert.Contains((int)instruction.OpCode, allowedOpcodes));
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

    private static string ToJavaScriptStringLiteral(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static ExecutionPlan GetScriptPlan(string source)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var cache = ((IAstCacheable<ScriptPlanCache>)pipeline.Analyzed).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private static ExecutionPlan GetClassConstructorPlan(string source, string className)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<ClassDeclaration>(
            pipeline.Analyzed.Body.Single(node =>
                node is ClassDeclaration classDeclaration &&
                classDeclaration.Name.Name == className));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Definition.Constructor).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private static ExecutionPlan GetClassMethodPlan(string source, string className, string methodName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<ClassDeclaration>(
            pipeline.Analyzed.Body.Single(node =>
                node is ClassDeclaration classDeclaration &&
                classDeclaration.Name.Name == className));
        var method = Assert.Single(declaration.Definition.Members.Where(member => member.Name == methodName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)method.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private static void SetSlot(UnifiedBytecodeProgram program, JsValue[] slots, string name, JsValue value)
    {
        var slotIndex = program.SlotNames.IndexOf(name);
        Assert.True(slotIndex >= 0, $"Expected unified bytecode slot '{name}'.");
        slots[slotIndex] = value;
    }

    private void AssertProductionRouted(string functionName)
    {
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"unified-bytecode-production-fast-path func={functionName}",
                StringComparison.Ordinal));
    }

    // This-binding widening proof pack (issue #2633 / ADR 0279)

    [Fact]
    public void Evaluate_ClassMethodThisPropertyRead_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetClassMethodPlan("""
            class Point {
                getX() {
                    return this.x;
                }
            }
            """,
            "Point",
            "getX");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadThis);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
    }

    [Fact]
    public void Evaluate_ClassMethodThisPropertyWrite_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetClassMethodPlan("""
            class Counter {
                inc() {
                    this.count = 1;
                }
            }
            """,
            "Counter",
            "inc");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadThis);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
    }

    [Fact]
    public void Evaluate_ClassMethodWithSuperPropertyWritesAndUpdates_AcceptsOwnedOpcodes()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                get value() { return this._value; }
                set value(next) { this._value = next; }
            }

            class Child extends Base {
                mutateSuper(name) {
                    super.value = 1;
                    super[name] = 2;
                    super.value++;
                    ++super[name];
                    return super.value;
                }
            }
            """,
            "Child",
            "mutateSuper");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedSuperProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedSuperProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateNamedSuperProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateComputedSuperProperty);
    }

    // Array-literal and object-literal operand widening (gh2705)

    [Fact]
    public void Evaluate_CallWithArrayLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendPair(receiver, a, b) {
                return receiver([a, b]);
            }
            """,
            "sendPair");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_CallWithObjectLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendConfig(receiver, x, y) {
                return receiver({ a: x, b: y });
            }
            """,
            "sendConfig");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_CallWithNestedArrayObjectLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, a, b) {
                return receiver({ items: [a, b] });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CreateArray);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayPush);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedObjectArrayLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, value) {
                return receiver([{ value }]);
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CreateObject);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayPush);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedObjectObjectLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, value) {
                return receiver({ inner: { value } });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(
            2,
            result.Program.Instructions.Count(instruction =>
                instruction.OpCode == UnifiedBytecodeOpCode.CreateObject));
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedTemplateObjectLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, name) {
                return receiver({ label: `hello ${name}` });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ToString);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.Binary &&
                           instruction.Operand == (int)BinaryOperator.Add);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedBinaryObjectLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, a, b) {
                return receiver({ total: a + b });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.Binary &&
                           instruction.Operand == (int)BinaryOperator.Add);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedBinaryArrayLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, a, b) {
                return receiver([a + b]);
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.Binary &&
                           instruction.Operand == (int)BinaryOperator.Add);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayPush);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedComputedObjectBinaryValueArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, key, a, b) {
                return receiver({ [key]: a + b });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.Binary &&
                           instruction.Operand == (int)BinaryOperator.Add);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedUnaryObjectLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, value) {
                return receiver({
                    plus: +value,
                    minus: -value,
                    not: !value,
                    bit: ~value,
                    voided: void value
                });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryPlus);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryMinus);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryLogicalNot);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryBitwiseNot);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryVoid);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedUnaryArrayLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, value) {
                return receiver([-value, !value]);
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryMinus);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryLogicalNot);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayPush);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedComputedObjectUnaryValueArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, key, value) {
                return receiver({ [key]: ~value });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryBitwiseNot);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedTypeOfObjectLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, value) {
                return receiver({ kind: typeof value });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.TypeOfIdentifier);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedTypeOfBinaryObjectLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, value) {
                return receiver({ kind: typeof (value + 1) });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.Binary &&
                           instruction.Operand == (int)BinaryOperator.Add);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.TypeOf);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedTypeOfArrayLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, value) {
                return receiver([typeof value]);
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.TypeOfIdentifier);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayPush);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedComputedObjectTypeOfValueArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, key, value) {
                return receiver({ [key]: typeof value });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.TypeOfIdentifier);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedNamedPropertyReadObjectLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, box) {
                return receiver({ value: box.count });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedNamedPropertyReadArrayLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, box) {
                return receiver([box.count]);
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayPush);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedComputedPropertyReadObjectLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, box, key) {
                return receiver({ value: box[key] });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.RequireObjectCoercible);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedComputedObjectPropertyReadValueArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, outKey, box, inKey) {
                return receiver({ [outKey]: box[inKey] });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedLogicalObjectLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, left, right) {
                return receiver({ value: left && right });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitFalse);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedLogicalArrayLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, left, right) {
                return receiver([left && right]);
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitFalse);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayPush);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedConditionalObjectLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, condition, consequent, alternate) {
                return receiver({ value: condition ? consequent : alternate });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedConditionalArrayLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, condition, consequent, alternate) {
                return receiver([condition ? consequent : alternate]);
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayPush);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithLogicalComputedObjectKeyArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, left, right, value) {
                return receiver({ [left && right]: value });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitFalse);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithConditionalComputedObjectKeyArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, condition, consequent, alternate, value) {
                return receiver({ [condition ? consequent : alternate]: value });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithConditionalObjectSpreadSourceArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, condition, consequent, alternate) {
                return receiver({ ...(condition ? consequent : alternate) });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ObjectSpread);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithConditionalArraySpreadSourceArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, condition, consequent, alternate) {
                return receiver([...(condition ? consequent : alternate)]);
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArraySpread);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithLogicalObjectSpreadSourceArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, left, right) {
                return receiver({ ...(left && right) });
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitFalse);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ObjectSpread);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithLogicalArraySpreadSourceArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, left, right) {
                return receiver([...(left && right)]);
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitFalse);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArraySpread);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithNestedBinaryTemplateArrayLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendNested(receiver, a, b) {
                return receiver([`total: ${a + b}`]);
            }
            """,
            "sendNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ToString);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.Binary &&
                           instruction.Operand == (int)BinaryOperator.Add);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayPush);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_CallWithEmptyArrayArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendEmpty(receiver) {
                return receiver([]);
            }
            """,
            "sendEmpty");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_CallWithEmptyObjectArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendEmptyObj(receiver) {
                return receiver({});
            }
            """,
            "sendEmptyObj");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_CallWithMixedScalarAndArrayLiteralArgs_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendMixed(receiver, a, b, c) {
                return receiver(a, [b, c]);
            }
            """,
            "sendMixed");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_PropertyReadBinaryExpressionWithArrayLiteralRhs_Accepts()
    {
        var plan = GetFunctionPlan("""
            function checkProp(obj, a, b) {
                return obj.value === [a, b];
            }
            """,
            "checkProp");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_PropertyReadBinaryExpressionWithObjectLiteralRhs_Accepts()
    {
        var plan = GetFunctionPlan("""
            function checkPropObj(obj, a) {
                return obj.value === { x: a };
            }
            """,
            "checkPropObj");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_CallWithHoleyArrayArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function sendHoley(receiver) {
                return receiver([1,,3]);
            }
            """,
            "sendHoley");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Theory]
    [InlineData("""
        function sendComputedIdKey(receiver, k, v) {
            return receiver({ [k]: v });
        }
        """, "sendComputedIdKey")]
    [InlineData("""
        function sendComputedStringKey(receiver, v) {
            return receiver({ ["name"]: v });
        }
        """, "sendComputedStringKey")]
    [InlineData("""
        function sendComputedNumKey(receiver, v) {
            return receiver({ [0]: v });
        }
        """, "sendComputedNumKey")]
    [InlineData("""
        function sendMixedStaticAndComputed(receiver, k, x, y) {
            return receiver({ a: x, [k]: y });
        }
        """, "sendMixedStaticAndComputed")]
    public void Evaluate_CallWithSimpleComputedKeyObjectArg_Accepts(string source, string functionName)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
    }

    [Fact]
    public void Evaluate_CallWithBinaryComputedKeyObjectArg_Accepts()
    {
        var plan = GetFunctionPlan("""
        function sendBinaryExprKey(receiver, a, b, v) {
            return receiver({ [a + b]: v });
        }
        """, "sendBinaryExprKey");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Binary && instruction.Operand == (int)BinaryOperator.Add);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
    }

    [Fact]
    public void Evaluate_CallWithCallExpressionComputedKeyObjectArg_Accepts()
    {
        var plan = GetFunctionPlan("""
        function sendCallExprKey(receiver, fn, v) {
            return receiver({ [fn()]: v });
        }
        """, "sendCallExprKey");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
    }

    [Fact]
    public void Evaluate_ObjectLiteralShorthandProperties_Accepts()
    {
        var plan = GetFunctionPlan("""
            function build(a, b) {
                return { a, b };
            }
            """,
            "build");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
    }

    [Fact]
    public void Evaluate_ObjectLiteralWithAnonymousFunctionValue_AcceptsWithNameInferenceFlag()
    {
        // AC-1: { key: function() {} } must be accepted (AllowNameInference on DefineObjectProperty).
        var plan = GetFunctionPlan("""
            function anonymousFunctionValue() {
                return { value: function() {} };
            }
            """,
            "anonymousFunctionValue");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_ObjectLiteralWithComputedKeyAnonymousFunctionValue_AcceptsWithNameInferenceFlag()
    {
        // AC-2: { [expr]: function() {} } must be accepted (AllowNameInference on DefineComputedObjectProperty).
        var plan = GetFunctionPlan("""
            function computedKeyFunctionValue(k) {
                return { [k]: function() {} };
            }
            """,
            "computedKeyFunctionValue");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_CallWithTemplateLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function log(receiver, name) {
                return receiver(`hello ${name}`);
            }
            """,
            "log");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_CallWithPureTextTemplateLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function logStatic(receiver) {
                return receiver(`hello world`);
            }
            """,
            "logStatic");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_CallWithMultiSubstitutionTemplateLiteralArg_Accepts()
    {
        var plan = GetFunctionPlan("""
            function logFull(receiver, first, last) {
                return receiver(`hello ${first} ${last}!`);
            }
            """,
            "logFull");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_CallWithBinaryTemplateLiteralSubstitution_Accepts()
    {
        var plan = GetFunctionPlan("""
            function logExpr(receiver, a, b) {
                return receiver(`result: ${a + b}`);
            }
            """,
            "logExpr");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Binary && instruction.Operand == (int)BinaryOperator.Add);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ToString);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_NamedPropertyWriteWithTemplateLiteralRhs_Accepts()
    {
        var plan = GetFunctionPlan("""
            function setLabel(box, name) {
                return box.label = `hello ${name}`;
            }
            """,
            "setLabel");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_PropertyReadBinaryExpressionWithTemplateLiteralRhs_Accepts()
    {
        var plan = GetFunctionPlan("""
            function checkLabel(box, name) {
                return box.label === `hello ${name}`;
            }
            """,
            "checkLabel");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_NamedCompoundPropertyWriteWithTemplateLiteralRhs_Accepts()
    {
        var plan = GetFunctionPlan("""
            function appendLabel(box, suffix) {
                return box.label += ` ${suffix}`;
            }
            """,
            "appendLabel");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    // This-base compound write proof pack (ADR 0238)

    [Fact]
    public void Evaluate_ThisBaseNamedCompoundPropertyWriteCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetClassMethodPlan("""
            class Counter {
                add(value) {
                    return this.count += value;
                }
            }
            """,
            "Counter",
            "add");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadThis);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Equal("count", Assert.Single(result.Program.StringConstants));
    }

    [Fact]
    public void Evaluate_ThisBaseComputedCompoundPropertyWriteCandidate_AcceptsOwnedPropertyOpcode()
    {
        var plan = GetClassMethodPlan("""
            class Counter {
                addAt(key, value) {
                    return this[key] += value;
                }
            }
            """,
            "Counter",
            "addAt");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadThis);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Theory]
    [InlineData("+=", (int)BinaryOperator.Add)]
    [InlineData("-=", (int)BinaryOperator.Subtract)]
    [InlineData("*=", (int)BinaryOperator.Multiply)]
    [InlineData("/=", (int)BinaryOperator.Divide)]
    [InlineData("%=", (int)BinaryOperator.Modulo)]
    [InlineData("**=", (int)BinaryOperator.Power)]
    [InlineData("&=", (int)BinaryOperator.BitwiseAnd)]
    [InlineData("|=", (int)BinaryOperator.BitwiseOr)]
    [InlineData("^=", (int)BinaryOperator.BitwiseXor)]
    [InlineData("<<=", (int)BinaryOperator.LeftShift)]
    [InlineData(">>=", (int)BinaryOperator.RightShift)]
    [InlineData(">>>=", (int)BinaryOperator.UnsignedRightShift)]
    public void Evaluate_NamedCompoundPropertyWrite_AllProductionOperators_Accept(string op, int expectedOperator)
    {
        var plan = GetFunctionPlan($$"""
            function write(box, value) {
                return box.count {{op}} value;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: var operand } && operand == expectedOperator);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
    }

    [Theory]
    [InlineData("+=", (int)BinaryOperator.Add)]
    [InlineData("-=", (int)BinaryOperator.Subtract)]
    [InlineData("*=", (int)BinaryOperator.Multiply)]
    [InlineData("/=", (int)BinaryOperator.Divide)]
    [InlineData("%=", (int)BinaryOperator.Modulo)]
    [InlineData("**=", (int)BinaryOperator.Power)]
    [InlineData("&=", (int)BinaryOperator.BitwiseAnd)]
    [InlineData("|=", (int)BinaryOperator.BitwiseOr)]
    [InlineData("^=", (int)BinaryOperator.BitwiseXor)]
    [InlineData("<<=", (int)BinaryOperator.LeftShift)]
    [InlineData(">>=", (int)BinaryOperator.RightShift)]
    [InlineData(">>>=", (int)BinaryOperator.UnsignedRightShift)]
    public void Evaluate_ThisBaseNamedCompoundPropertyWrite_AllProductionOperators_Accept(string op, int expectedOperator)
    {
        var plan = GetClassMethodPlan($$"""
            class Counter {
                apply(value) {
                    return this.count {{op}} value;
                }
            }
            """,
            "Counter",
            "apply");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadThis);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: var operand } && operand == expectedOperator);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
    }

    [Fact]
    public void Evaluate_NestedNamedPropertyWrite_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function writeNested(box, value) {
                return box.child.value = value;
            }
            """,
            "writeNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Equal(new[] { "child", "value" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_NestedNamedComputedPropertyWrite_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function writeNestedComputed(box, key, value) {
                box.child[key] = value;
            }
            """,
            "writeNestedComputed");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Fact]
    public void Evaluate_NestedNamedComputedPropertyWriteWithBinaryKey_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function writeNestedComputed(box, key, suffix, value) {
                box.child[key + suffix] = value;
            }
            """,
            "writeNestedComputed");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: (int)BinaryOperator.Add });
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Theory]
    [InlineData("box.child[key]++")]
    [InlineData("++box.child[key]")]
    [InlineData("box.child[key]--")]
    [InlineData("--box.child[key]")]
    public void Evaluate_NestedNamedComputedPropertyUpdate_AcceptsOwnedPropertyOpcodes(string expression)
    {
        var plan = GetFunctionPlan($$"""
            function update(box, key) {
                return {{expression}};
            }
            """,
            "update");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateComputedProperty);
        // The named receiver prefix must collapse to a single GetNamedProperty hop, not a
        // computed key-span read.
        Assert.DoesNotContain(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
    }

    [Fact]
    public void Evaluate_DeepNestedNamedComputedPropertyUpdate_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function update(box, key) {
                return box.child.branch[key]++;
            }
            """,
            "update");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(
            2,
            result.Program.Instructions.Count(static instruction =>
                instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty));
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateComputedProperty);
    }

    [Fact]
    public void Evaluate_NestedNamedComputedPropertyUpdateWithBinaryKey_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function update(box, key) {
                return box.child[key + 1]++;
            }
            """,
            "update");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: (int)BinaryOperator.Add });
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateComputedProperty);
    }

    [Theory]
    [InlineData("box[k1].child[k2]++")]
    [InlineData("--box[k1].child[k2]")]
    [InlineData("box[k1].child[k2]--")]
    [InlineData("++box[k1].child[k2]")]
    public void Evaluate_ComputedPrefixComputedPropertyUpdate_AcceptsOwnedPropertyOpcodes(string expression)
    {
        // A23: a computed receiver prefix (`box[k1].child[k2]++`) resolves the prefix once via the
        // shared computed-read span helper, then performs a computed update. It now routes through
        // production unified bytecode.
        var plan = GetFunctionPlan($$"""
            function update(box, k1, k2) {
                return {{expression}};
            }
            """,
            "update");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateComputedProperty);
    }

    [Theory]
    [InlineData("box[k1].child++")]
    [InlineData("--box[k1].child")]
    public void Evaluate_ComputedPrefixNamedPropertyUpdate_AcceptsOwnedPropertyOpcodes(string expression)
    {
        // A23: a computed receiver prefix (`box[k1].child++`) feeding a NAMED update. The
        // computed-read span is the whole receiver and the named property is the update target.
        var plan = GetFunctionPlan($$"""
            function update(box, k1) {
                return {{expression}};
            }
            """,
            "update");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateNamedProperty);
    }

    [Fact]
    public void Evaluate_ComputedPrefixPropertyUpdateWithCallInPrefix_Declines()
    {
        // A call hop inside the receiver prefix (`box[k1]().child[k2]++`) is not a simple read
        // span and must decline. The call is caught first by the invocation boundary, so the
        // decline surfaces as CallDependency -- the contract is that it stays OUT of production.
        var plan = GetFunctionPlan("""
            function update(box, k1, k2) {
                return box[k1]().child[k2]++;
            }
            """,
            "update");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.CallDependency, result.Code);
    }

    [Fact]
    public void Evaluate_NestedNamedComputedPropertyWriteWithTernaryKey_AcceptsControlExpressionKeySpan()
    {
        // A ternary computed key (`box.child[cond ? a : b] = value`) introduces branch
        // control flow into the key span. IsSupportedComputedPropertyKeySpan now admits a
        // whole-span control expression by delegating to the dedicated control-flow key
        // emitter, so this routes through production unified bytecode.
        var plan = GetFunctionPlan("""
            function write(box, cond, a, b, value) {
                box.child[cond ? a : b] = value;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            instruction => instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Fact]
    public void Evaluate_ComputedPrefixComputedPropertyWrite_DeclinesWithPropertyWriteDependency()
    {
        // A computed receiver prefix (`box[k1].child[k2] = v`) is outside the
        // nested-NAMED-prefix computed-write boundary and must still decline.
        var plan = GetFunctionPlan("""
            function writeComputedPrefix(box, k1, k2, value) {
                box[k1].child[k2] = value;
            }
            """,
            "writeComputedPrefix");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency, result.Code);
    }

    [Fact]
    public void Evaluate_ComputedPrefixNamedPropertyWrite_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function writeNamedThroughComputed(box, key, value) {
                box[key].child = value;
            }
            """,
            "writeNamedThroughComputed");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
    }

    [Fact]
    public void Evaluate_DoubleComputedPrefixNamedPropertyWrite_DeclinesWithPropertyWriteDependency()
    {
        // A double computed receiver prefix (`box[k1][k2].child = v`) is outside the
        // single-computed-read prefix boundary and must still decline.
        var plan = GetFunctionPlan("""
            function writeDoubleComputed(box, k1, k2, value) {
                box[k1][k2].child = value;
            }
            """,
            "writeDoubleComputed");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency, result.Code);
    }

    [Fact]
    public void Evaluate_NestedNamedComputedCompoundPropertyWrite_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function write(box, key, value) {
                return box.child[key] += value;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Fact]
    public void Evaluate_DeepNestedNamedComputedCompoundPropertyWrite_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function write(box, key, value) {
                return box.child.branch[key] += value;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(
            2,
            result.Program.Instructions.Count(static instruction =>
                instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty));
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Theory]
    [InlineData("&&=")]
    [InlineData("||=")]
    [InlineData("??=")]
    public void Evaluate_NestedNamedComputedLogicalPropertyWrite_AcceptsOwnedPropertyOpcodes(string op)
    {
        var plan = GetFunctionPlan($$"""
            function write(box, key, value) {
                return box.child[key] {{op}} value;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Fact]
    public void Evaluate_ComputedPrefixComputedCompoundPropertyWrite_DeclinesWithPropertyWriteDependency()
    {
        // A computed receiver prefix (`box[k1].child[k2] += v`) is outside the
        // nested-NAMED-prefix computed compound-write boundary and must still decline.
        var plan = GetFunctionPlan("""
            function writeComputedPrefix(box, k1, k2, value) {
                box[k1].child[k2] += value;
            }
            """,
            "writeComputedPrefix");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency, result.Code);
    }

    [Fact]
    public void Evaluate_ComputedPropertyWriteWithExpressionKey_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function writeComputed(box, key, suffix, value) {
                return box[key + suffix] = value;
            }
            """,
            "writeComputed");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: (int)BinaryOperator.Add });
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Fact]
    public void Evaluate_NestedNamedPropertyUpdate_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetFunctionPlan("""
            function updateNested(box) {
                return ++box.child.count;
            }
            """,
            "updateNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.UpdateNamedProperty);
        Assert.Equal(new[] { "child", "count" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_NestedNamedCompoundPropertyWrite_AcceptsOwnedPropertyOpcodes()
    {
        var plan = GetClassMethodPlan("""
            class Counter {
                addNested(value) {
                    return this.child.count += value;
                }
            }
            """,
            "Counter",
            "addNested");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Equal(new[] { "child", "count" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_LogicalAndExpression_AcceptsWithShortCircuitFalseOpcode()
    {
        var plan = GetFunctionPlan("""
            function andExpr(a, b) {
                return a && b;
            }
            """,
            "andExpr");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitFalse);
    }

    [Fact]
    public void Evaluate_LogicalOrExpression_AcceptsWithShortCircuitTrueOpcode()
    {
        var plan = GetFunctionPlan("""
            function orExpr(a, b) {
                return a || b;
            }
            """,
            "orExpr");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitTrue);
    }

    [Fact]
    public void Evaluate_NullishCoalescingExpression_AcceptsWithShortCircuitNotNullishOpcode()
    {
        var plan = GetFunctionPlan("""
            function nullishExpr(a, b) {
                return a ?? b;
            }
            """,
            "nullishExpr");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish);
    }

    [Fact]
    public void Evaluate_LogicalAnd_LiteralOperand_Accepts()
    {
        var plan = GetFunctionPlan("""
            function f(a) {
                return a && 42;
            }
            """,
            "f");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_NullishCoalescing_LiteralFallback_Accepts()
    {
        var plan = GetFunctionPlan("""
            function f(a) {
                return a ?? 0;
            }
            """,
            "f");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_OptionalNamedPropertyReadExpressionPlan_AcceptsWithGetNamedPropertyOptionalOpcode()
    {
        // gh2771: simple a?.b form is admitted through the production unified bytecode VM.
        var plan = GetFunctionPlan("""
            function optChain(obj) {
                return obj?.value;
            }
            """,
            "optChain");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyOptional);
    }

    [Fact]
    public void Evaluate_OptionalComputedPropertyReadExpressionPlan_AcceptsWithJumpIfNullishReplaceUndefinedOpcode()
    {
        // gh2771: simple a?.[k] form is admitted through the production unified bytecode VM.
        var plan = GetFunctionPlan("""
            function optComputedChain(box, key) {
                return box?.[key];
            }
            """,
            "optComputedChain");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
    }

    [Fact]
    public void Evaluate_OptionalComputedPropertyReadWithRichKeyExpressionPlan_AcceptsWithJumpIfNullishReplaceUndefinedOpcode()
    {
        var plan = GetFunctionPlan("""
            function optComputedChain(box, left, right) {
                return box?.[left + right];
            }
            """,
            "optComputedChain");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Binary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
    }

    [Fact]
    public void Evaluate_OptionalComputedPropertyReadWithNamedPrefix_AcceptsWithJumpIfNullishReplaceUndefinedOpcode()
    {
        var plan = GetFunctionPlan("""
            function optComputedPrefixed(box, key) {
                return box.child?.[key];
            }
            """,
            "optComputedPrefixed");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
    }

    [Fact]
    public void Evaluate_OptionalNamedPropertyReadChainExpressionPlan_AcceptsWithJumpIfNullishReplaceUndefined()
    {
        // AC-1: a?.b.c with an activation-resolved base is admitted.
        var plan = GetFunctionPlan("""
            function optChainRead(obj) {
                return obj?.value.length;
            }
            """,
            "optChainRead");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
    }

    [Fact]
    public void Evaluate_OptionalNamedThenComputedReadExpressionPlan_AcceptsWithJumpIfNullishReplaceUndefined()
    {
        // AC-1 variant: a?.b[k] — optional named then computed.
        var plan = GetFunctionPlan("""
            function optChainComputed(obj, key) {
                return obj?.items[key];
            }
            """,
            "optChainComputed");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
    }

    [Fact]
    public void Evaluate_OptionalNamedThenComputedReadWithRichKeyExpressionPlan_AcceptsWithJumpIfNullishReplaceUndefined()
    {
        // AC-1 variant: a?.b[left + right] — optional named then rich computed key.
        var plan = GetFunctionPlan("""
            function optChainComputed(obj, left, right) {
                return obj?.items[left + right];
            }
            """,
            "optChainComputed");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Binary);
    }

    [Fact]
    public void Evaluate_OptionalNamedThenComputedReadWithTrailingNamedContinuation_Accepts()
    {
        // AC-2: a?.b[left + right].c keeps the already VM-executable trailing read continuation.
        var plan = GetFunctionPlan("""
            function optChainComputed(obj, left, right) {
                return obj?.items[left + right].value;
            }
            """,
            "optChainComputed");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Binary);
        Assert.True(
            result.Program.Instructions.Count(static instruction =>
                instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty) >= 2);
    }

    [Fact]
    public void Evaluate_OptionalNamedThenComputedReadWithNamedPrefix_Accepts()
    {
        var plan = GetFunctionPlan("""
            function optChainComputed(obj, key) {
                return obj.nested?.items[key].value;
            }
            """,
            "optChainComputed");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.True(
            result.Program.Instructions.Count(static instruction =>
                instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty) >= 3);
    }

    [Fact]
    public void Evaluate_OptionalNamedPropertyReadChainWithNamedPrefix_Accepts()
    {
        // a.b?.c.d keeps the activation-resolved root, lowers the named prefix as a plain
        // receiver read, then uses the same jump-owned optional-chain boundary as a?.b.c.
        var plan = GetFunctionPlan("""
            function optChainNonResolved(obj) {
                return obj.nested?.value.length;
            }
            """,
            "optChainNonResolved");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.True(
            result.Program.Instructions.Count(static instruction =>
                instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty) >= 3);
    }

    [Fact]
    public void Evaluate_ChainedOptionalPropertyReadExpressionPlan_AcceptsWithJumpIfNullishReplaceUndefinedOpcode()
    {
        // Chained optional forms (a?.b?.c) lower to a jump-based form: each optional hop emits a
        // JumpIfNullishReplaceUndefined targeting the chain end, followed by plain GetNamedProperty reads.
        var plan = GetFunctionPlan("""
            function chainedOptChain(a) {
                return a?.b?.c;
            }
            """,
            "chainedOptChain");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
    }

    [Fact]
    public void Evaluate_OptionalThenRegularPropertyChain_AcceptsWithJumpIfNullishReplaceUndefinedOpcode()
    {
        // AC-5: a?.b.c — first hop is optional, second hop is regular (emits
        // ShortCircuitOnNullishTarget:true). The widened eligibility admits this chain and the
        // compiler lowers it to JumpIfNullishReplaceUndefined(END) + plain GetNamedProperty reads.
        var plan = GetFunctionPlan("""
            function f(a) {
                return a?.b.c;
            }
            """,
            "f");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        // The chain must NOT use GetNamedPropertyOptional — the jump owns the short-circuit so the
        // intermediate reads stay plain (real-undefined intermediates must still throw, AC-3).
        Assert.DoesNotContain(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyOptional);
    }

    [Fact]
    public void Evaluate_NonOptionalNamedPropertyReadChain_IsUnaffectedByOptionalChainAdmission()
    {
        // Admitting a?.b must not disturb regular a.b.c eligibility.
        var plan = GetFunctionPlan("""
            function readChain(a) {
                return a.b.c;
            }
            """,
            "readChain");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.DoesNotContain(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyOptional);
    }

    [Fact]
    public void Evaluate_NonOptionalComputedPropertyRead_IsUnaffectedByOptionalChainAdmission()
    {
        // Admitting a?.[k] must not disturb regular a[k] eligibility.
        var plan = GetFunctionPlan("""
            function readComputed(a, k) {
                return a[k];
            }
            """,
            "readComputed");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
    }

    // Literal-operand proof pack for &&/||/?? (ADR 0238 batch-5)

    [Fact]
    public void Evaluate_LogicalAndExpression_LiteralRight_AcceptsWithShortCircuitFalseOpcode()
    {
        var plan = GetFunctionPlan("""
            function andLiteral(a) {
                return a && 42;
            }
            """,
            "andLiteral");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitFalse);
    }

    [Fact]
    public void Evaluate_LogicalOrExpression_LiteralRight_AcceptsWithShortCircuitTrueOpcode()
    {
        var plan = GetFunctionPlan("""
            function orLiteral(a) {
                return a || 0;
            }
            """,
            "orLiteral");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitTrue);
    }

    [Fact]
    public void Evaluate_NullishCoalescingExpression_LiteralRight_AcceptsWithShortCircuitNotNullishOpcode()
    {
        var plan = GetFunctionPlan("""
            function nullishLiteral(a) {
                return a ?? 0;
            }
            """,
            "nullishLiteral");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish);
    }

    // This-property-operand proof pack for &&/||/?? (ADR 0238 batch-5)

    [Fact]
    public void Evaluate_LogicalAndExpression_ThisPropertyLeft_AcceptsWithShortCircuitFalseOpcode()
    {
        var plan = GetClassMethodPlan("""
            class Guard {
                check(b) {
                    return this.enabled && b;
                }
            }
            """,
            "Guard",
            "check");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadThis);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitFalse);
    }

    [Fact]
    public void Evaluate_LogicalOrExpression_ThisPropertyLeft_AcceptsWithShortCircuitTrueOpcode()
    {
        var plan = GetClassMethodPlan("""
            class Guard {
                fallback(b) {
                    return this.value || b;
                }
            }
            """,
            "Guard",
            "fallback");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadThis);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitTrue);
    }

    [Fact]
    public void Evaluate_NullishCoalescingExpression_ThisPropertyLeft_AcceptsWithShortCircuitNotNullishOpcode()
    {
        var plan = GetClassMethodPlan("""
            class Box {
                resolve(b) {
                    return this.cached ?? b;
                }
            }
            """,
            "Box",
            "resolve");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadThis);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish);
    }

    // Optional-chain-as-operand admission for &&/||/?? and ?: (ADR 0238 batch-5 follow-up)

    [Fact]
    public void Evaluate_LogicalAndExpression_WithOptionalChainOperand_Accepts()
    {
        var plan = GetFunctionPlan("""
            function andOpt(a, obj) {
                return a && obj?.value;
            }
            """,
            "andOpt");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitFalse);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyOptional);
    }

    [Fact]
    public void Evaluate_LogicalOrExpression_WithOptionalChainOperand_Accepts()
    {
        var plan = GetFunctionPlan("""
            function orOpt(a, obj) {
                return a || obj?.value;
            }
            """,
            "orOpt");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitTrue);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyOptional);
    }

    [Fact]
    public void Evaluate_NullishCoalescingExpression_WithOptionalChainOperand_Accepts()
    {
        var plan = GetFunctionPlan("""
            function nullishOpt(a, obj) {
                return a ?? obj?.value;
            }
            """,
            "nullishOpt");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyOptional);
    }

    [Fact]
    public void Evaluate_LogicalAndExpression_WithOptionalComputedChainOperand_Accepts()
    {
        var plan = GetFunctionPlan("""
            function andComputed(a, obj, key) {
                return a && obj?.[key];
            }
            """,
            "andComputed");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
    }

    // Conditional (ternary) expression admission — ADR 0294

    [Fact]
    public void Evaluate_ConditionalExpression_SlotCondition_Accepts()
    {
        var plan = GetFunctionPlan("""
            function ternary(a, b, c) {
                return a ? b : c;
            }
            """,
            "ternary");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Jump);
    }

    [Fact]
    public void Evaluate_ConditionalExpression_LiteralConsequentAndAlternate_Accepts()
    {
        var plan = GetFunctionPlan("""
            function clamp(a) {
                return a > 0 ? 1 : 0;
            }
            """,
            "clamp");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_ConditionalExpression_WithOptionalChainBranch_Accepts()
    {
        var plan = GetFunctionPlan("""
            function ternaryOpt(a, obj) {
                return a ? obj?.value : 0;
            }
            """,
            "ternaryOpt");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyOptional);
    }

    [Fact]
    public void Evaluate_ConditionalExpression_NestedTernary_Accepts()
    {
        var plan = GetFunctionPlan("""
            function classify(c1, c2, a, b, d) {
                return c1 ? c2 ? a : b : d;
            }
            """,
            "classify");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Jump);
    }

    [Fact]
    public void Evaluate_ConditionalExpression_ThisPropertyConditionAndArms_Accepts()
    {
        var plan = GetClassMethodPlan("""
            class Toggle {
                select(other) {
                    return this.flag ? this.a : other;
                }
            }
            """,
            "Toggle",
            "select");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
    }

    [Fact]
    public void Evaluate_LogicalAndAssignment_SlotBased_Accepts()
    {
        var plan = GetFunctionPlan("""
            function andAssign(x, y) {
                x &&= y;
                return x;
            }
            """,
            "andAssign");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_LogicalOrAssignment_SlotBased_Accepts()
    {
        var plan = GetFunctionPlan("""
            function orAssign(x, y) {
                x ||= y;
                return x;
            }
            """,
            "orAssign");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_NullishAssignment_SlotBased_Accepts()
    {
        var plan = GetFunctionPlan("""
            function nullishAssign(x, y) {
                x ??= y;
                return x;
            }
            """,
            "nullishAssign");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_LogicalAndAssignment_ThisPropertyBase_AcceptsWithOwnedOpcodes()
    {
        var plan = GetClassMethodPlan("""
            class Obj {
                method(value) {
                    this.x &&= value;
                }
            }
            """,
            "Obj",
            "method");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitFalse);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
    }

    [Fact]
    public void Evaluate_LogicalAndAssignment_DynamicIdentifierBase_AcceptsWhenDynamicReadsAreAdmitted()
    {
        var plan = GetFunctionPlan("""
            function write() {
                return outer.value &&= 43;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitFalse);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Equal(new[] { "outer", "value" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_LogicalOrAssignment_ThisPropertyBase_AcceptsWithOwnedOpcodes()
    {
        var plan = GetClassMethodPlan("""
            class Obj {
                method(value) {
                    this.x ||= value;
                }
            }
            """,
            "Obj",
            "method");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitTrue);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
    }

    [Fact]
    public void Evaluate_LogicalOrAssignment_ComputedDynamicIdentifierBase_AcceptsWhenDynamicReadsAreAdmitted()
    {
        var plan = GetFunctionPlan("""
            function write() {
                return outer[key] ||= 43;
            }
            """,
            "write");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitTrue);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
        Assert.Equal(new[] { "outer", "key" }, result.Program.StringConstants);
    }

    [Fact]
    public void Evaluate_NullishAssignment_ThisPropertyBase_AcceptsWithOwnedOpcodes()
    {
        var plan = GetClassMethodPlan("""
            class Obj {
                method(value) {
                    this.x ??= value;
                }
            }
            """,
            "Obj",
            "method");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
    }

    [Theory]
    [InlineData(
        """
        function logicalAndComputedWrite(box, key, value) {
            return box[key] &&= value;
        }
        """,
        "logicalAndComputedWrite",
        (int)UnifiedBytecodeOpCode.JumpIfShortCircuitFalse)]
    [InlineData(
        """
        function logicalOrComputedWrite(box, key, value) {
            return box[key] ||= value;
        }
        """,
        "logicalOrComputedWrite",
        (int)UnifiedBytecodeOpCode.JumpIfShortCircuitTrue)]
    [InlineData(
        """
        function logicalNullishComputedWrite(box, key, value) {
            return box[key] ??= value;
        }
        """,
        "logicalNullishComputedWrite",
        (int)UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish)]
    public void Evaluate_ComputedLogicalAssignment_AcceptsWithOwnedOpcodes(
        string source,
        string functionName,
        int expectedJumpOpCode)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == (UnifiedBytecodeOpCode)expectedJumpOpCode);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Fact]
    public void Evaluate_ComputedLogicalAssignment_ShortCircuitCleanup_DropsTargetAndKeyFromStack()
    {
        var plan = GetFunctionPlan("""
            function logicalAndComputedWrite(box, key, value) {
                return box[key] &&= value;
            }
            """,
            "logicalAndComputedWrite");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);

        var jumpIndex = -1;
        for (var i = 0; i < result.Program.Instructions.Length; i++)
        {
            if (result.Program.Instructions[i].OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuitFalse)
            {
                jumpIndex = i;
                break;
            }
        }
        Assert.True(jumpIndex >= 0);

        var cleanupPatternFound = false;
        for (var i = jumpIndex + 1; i + 3 < result.Program.Instructions.Length; i++)
        {
            if (result.Program.Instructions[i].OpCode == UnifiedBytecodeOpCode.SwapTopTwo
                && result.Program.Instructions[i + 1].OpCode == UnifiedBytecodeOpCode.Pop
                && result.Program.Instructions[i + 2].OpCode == UnifiedBytecodeOpCode.SwapTopTwo
                && result.Program.Instructions[i + 3].OpCode == UnifiedBytecodeOpCode.Pop)
            {
                cleanupPatternFound = true;
                break;
            }
        }

        Assert.True(
            cleanupPatternFound,
            string.Join(", ", result.Program.Instructions.Select(static instruction => instruction.OpCode.ToString())));
    }

    [Theory]
    [InlineData("&&=", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitFalse)]
    [InlineData("||=", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitTrue)]
    [InlineData("??=", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish)]
    public void Evaluate_ComputedLogicalAssignmentWithExpressionKey_AcceptsWithOwnedOpcodes(
        string logicalOperator,
        int expectedJumpOpCode)
    {
        var plan = GetFunctionPlan($$"""
            function logicalComputedWrite(box, key, suffix, value) {
                return box[key + suffix] {{logicalOperator}} value;
            }
            """,
            "logicalComputedWrite");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction is { OpCode: UnifiedBytecodeOpCode.Binary, Operand: (int)BinaryOperator.Add });
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == (UnifiedBytecodeOpCode)expectedJumpOpCode);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Theory]
    [InlineData("&&=", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitFalse)]
    [InlineData("||=", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitTrue)]
    [InlineData("??=", (int)UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish)]
    public void Evaluate_NestedNamedLogicalAssignment_AcceptsWithOwnedOpcodes(
        string logicalOperator,
        int expectedJumpOpCode)
    {
        var plan = GetFunctionPlan($$"""
            function logicalNestedWrite(box, value) {
                return box.child.value {{logicalOperator}} value;
            }
            """,
            "logicalNestedWrite");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == (UnifiedBytecodeOpCode)expectedJumpOpCode);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Equal(new[] { "child", "value" }, result.Program.StringConstants);
    }

    [Theory]
    [InlineData(
        """
        function logicalAndComputedThisKeyWrite(box, value) {
            return box[this] &&= value;
        }
        """,
        "logicalAndComputedThisKeyWrite",
        (int)UnifiedBytecodeOpCode.LoadThis)]
    [InlineData(
        """
        function logicalAndComputedNewTargetKeyWrite(box, value) {
            return box[new.target] &&= value;
        }
        """,
        "logicalAndComputedNewTargetKeyWrite",
        (int)UnifiedBytecodeOpCode.LoadNewTarget)]
    public void Evaluate_LogicalAndAssignment_ThisAndNewTargetComputedKeys_AcceptWithOwnedOpcodes(
        string source,
        string functionName,
        int expectedKeyLoadOpCode)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == (UnifiedBytecodeOpCode)expectedKeyLoadOpCode);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Fact]
    public void Evaluate_ComputedLogicalAssignmentWithBinaryRhs_AcceptsWithOwnedOpcodes()
    {
        var plan = GetFunctionPlan(
            """
            function logicalAndComputedComplexRhsWrite(box, key, value) {
                return box[key] &&= (value + 1);
            }
            """,
            "logicalAndComputedComplexRhsWrite");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Binary &&
            instruction.Operand == (int)BinaryOperator.Add);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    [Fact]
    public void Evaluate_LogicalAndAssignment_OptionalNamedBase_IsParserRejected()
    {
        Assert.Throws<NotSupportedException>(() => GetFunctionPlan(
            """
            function logicalAndOptionalWrite(box, value) {
                return box?.prop &&= value;
            }
            """,
            "logicalAndOptionalWrite"));
    }

    [Fact]
    public void EvaluateResumable_SwitchBody_AdmitsBreakableMarkers()
    {
        var plan = GetFunctionPlan("""
            function* gen(n) {
                yield 0;
                switch (n) {
                    case 1:
                        return 10;
                    default:
                        return 20;
                }
            }
            """,
            "gen");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Yield);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Return);
    }

}
