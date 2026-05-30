using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
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
                    UnifiedBytecodeOpCode.InitializeSlot,
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
    public void EvaluateResumable_YieldStar_DeclinesUntilDelegatedAbruptResumeIsModeled()
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

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("YieldStar", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_AsyncLikeGeneratorActivation_DeclinesBeforeExecution()
    {
        var plan = GetFunctionPlan("""
            function* gen() {
                yield 1;
            }
            """,
            "gen");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true, IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.AsyncLikeFunction, result.Code);
        Assert.Contains("Async-like", result.Reason, StringComparison.Ordinal);
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
    [InlineData(
        """
        function destructure(source) {
            {
                let { value } = source;
                return value;
            }
        }
        """,
        "destructure",
        (int)UnifiedBytecodeProductionDeclineCode.DestructuringDependency)]
    [InlineData(
        """
        function directEval() {
            eval("var value = 1");
            return value;
        }
        """,
        "directEval",
        (int)UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency)]
    public void Evaluate_UnsupportedDynamicAndDestructuringBlockShapes_StayDeclined(
        string source,
        string functionName,
        int expectedCode)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal((UnifiedBytecodeProductionDeclineCode)expectedCode, result.Code);
        Assert.NotEmpty(result.Reason);
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
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.StoreDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.TypeOfDynamicIdentifier);
        Assert.Contains(result.Program.Instructions, instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeleteDynamicIdentifier);
    }

    [Fact]
    public void Evaluate_WithThenOutsideDynamicIdentifier_DeclinesWithDynamicLookupDependency()
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
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency, result.Code);
        Assert.Contains("externalValue", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        """
        function spreadArray(source) {
            return [1, ...source];
        }
        """,
        "spreadArray")]
    [InlineData(
        """
        function spreadObject(source) {
            return { ...source };
        }
        """,
        "spreadObject")]
    [InlineData(
        """
        function methodObject() {
            return { value() { return 1; } };
        }
        """,
        "methodObject")]
    [InlineData(
        """
        function accessorObject() {
            return { get value() { return 1; } };
        }
        """,
        "accessorObject")]
    [InlineData(
        """
        function anonymousFunctionValue() {
            return { value: function() {} };
        }
        """,
        "anonymousFunctionValue")]
    public void Evaluate_ExcludedLiteralConstructionShapes_DeclineWithExplicitCode(
        string source,
        string functionName)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency, result.Code);
        Assert.NotEmpty(result.Reason);
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
    public void Evaluate_ComputedPropertyReadOutsideFirstBoundary_DeclinesWithBoundaryCode()
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

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.PropertyReadBoundaryOutOfScope, result.Code);
        Assert.Contains("RequireObjectCoercible", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        """
        function invokeSpread(helper, values) {
            return helper(...values);
        }
        """,
        "invokeSpread",
        (int)UnifiedBytecodeProductionDeclineCode.CallDependency)]
    [InlineData(
        """
        function invokeEval(source) {
            return eval(source);
        }
        """,
        "invokeEval",
        (int)UnifiedBytecodeProductionDeclineCode.CallDependency)]
    [InlineData(
        """
        function invokeComputedExpressionKey(box, left, right) {
            return box[left + right]();
        }
        """,
        "invokeComputedExpressionKey",
        (int)UnifiedBytecodeProductionDeclineCode.CallDependency)]
    [InlineData(
        """
        function invokeDeepComputedCallee(root, key, value) {
            return root.child.branch.leaf[key](value);
        }
        """,
        "invokeDeepComputedCallee",
        (int)UnifiedBytecodeProductionDeclineCode.PropertyReadBoundaryOutOfScope)]
    [InlineData(
        """
        function construct(ctor, value) {
            return new ctor(value);
        }
        """,
        "construct",
        (int)UnifiedBytecodeProductionDeclineCode.CallDependency)]
    [InlineData(
        """
        function remove(box) {
            return delete box.value;
        }
        """,
        "remove",
        (int)UnifiedBytecodeProductionDeclineCode.DeleteDependency)]
    [InlineData(
        """
        function readOptional(box) {
            return box?.value;
        }
        """,
        "readOptional",
        (int)UnifiedBytecodeProductionDeclineCode.OptionalChainDependency)]
    [InlineData(
        """
        function readOptionalComputed(box, key) {
            return box?.[key];
        }
        """,
        "readOptionalComputed",
        (int)UnifiedBytecodeProductionDeclineCode.OptionalChainDependency)]
    [InlineData(
        """
        function invokeOptionalReceiver(box) {
            return box?.read();
        }
        """,
        "invokeOptionalReceiver",
        (int)UnifiedBytecodeProductionDeclineCode.OptionalChainDependency)]
    [InlineData(
        """
        function invokeOptionalCallee(box) {
            return box.read?.();
        }
        """,
        "invokeOptionalCallee",
        (int)UnifiedBytecodeProductionDeclineCode.OptionalChainDependency)]
    [InlineData(
        """
        function invokeOptionalComputedCallee(box, key) {
            return box[key]?.();
        }
        """,
        "invokeOptionalComputedCallee",
        (int)UnifiedBytecodeProductionDeclineCode.OptionalChainDependency)]
    [InlineData(
        """
        function readLiteral(box) {
            return { ...box }.value;
        }
        """,
        "readLiteral",
        (int)UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency)]
    [InlineData(
        """
        function readDynamic(box) {
            return box[externalKey];
        }
        """,
        "readDynamic",
        (int)UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency)]
    [InlineData(
        """
        function readBinaryTarget(a, b) {
            return (a + b).value;
        }
        """,
        "readBinaryTarget",
        (int)UnifiedBytecodeProductionDeclineCode.PropertyReadBoundaryOutOfScope)]
    [InlineData(
        """
        function readComputedObjectLiteralKey(box) {
            return box[{ value: 1 }];
        }
        """,
        "readComputedObjectLiteralKey",
        (int)UnifiedBytecodeProductionDeclineCode.PropertyReadBoundaryOutOfScope)]
    [InlineData(
        """
        function readComputedSpreadKey(box, source) {
            return box[{ ...source }];
        }
        """,
        "readComputedSpreadKey",
        (int)UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency)]
    [InlineData(
        """
        function readComputedOnBase(box, key) {
            return box[key].value;
        }
        """,
        "readComputedOnBase",
        (int)UnifiedBytecodeProductionDeclineCode.PropertyReadBoundaryOutOfScope)]
    [InlineData(
        """
        function readComputedAfterNamed(box, key) {
            return box.child[key];
        }
        """,
        "readComputedAfterNamed",
        (int)UnifiedBytecodeProductionDeclineCode.PropertyReadBoundaryOutOfScope)]
    [InlineData(
        """
        function logicalWrite(box, value) {
            return box.value ||= value;
        }
        """,
        "logicalWrite",
        (int)UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency)]
    [InlineData(
        """
        var externalValue = 42;
        function dynamicValueWrite(box) {
            return box.value = externalValue;
        }
        """,
        "dynamicValueWrite",
        (int)UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency)]
    [InlineData(
        """
        function computedExpressionWrite(box, key, suffix, value) {
            return box[key + suffix] = value;
        }
        """,
        "computedExpressionWrite",
        (int)UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency)]
    [InlineData(
        """
        function complexCompoundWrite(box, value) {
            return box.child.value += value;
        }
        """,
        "complexCompoundWrite",
        (int)UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency)]
    [InlineData(
        """
        function destructureWrite(box, source) {
            ({ value: box.value } = source);
            return 0;
        }
        """,
        "destructureWrite",
        (int)UnifiedBytecodeProductionDeclineCode.DestructuringDependency)]
    public void Evaluate_PropertyReadAdjacentFamilies_DeclineWithExplicitCodes(
        string source,
        string functionName,
        int expectedCode)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal((UnifiedBytecodeProductionDeclineCode)expectedCode, result.Code);
    }

    [Fact]
    public void Evaluate_SuperPropertyAccess_DeclinesWithExplicitCode()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                get value() {
                    return 1;
                }
            }

            class Derived extends Base {
                read() {
                    return super.value;
                }
            }
            """,
            "Derived",
            "read");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.SuperPropertyDependency, result.Code);
        Assert.Contains("super", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_SuperCall_DeclinesWithExplicitCode()
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

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.CallDependency, result.Code);
        Assert.Contains("super call", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_PrivateFieldIn_DeclinesWithExplicitCode()
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

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.PrivateFieldDependency, result.Code);
        Assert.Contains("Private-field", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("update")]
    public void Evaluate_PrivateNamedPropertyAccess_DeclinesWithExplicitCode(string methodName)
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
            methodName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.PrivateFieldDependency, result.Code);
        Assert.Contains("Private-field", result.Reason, StringComparison.Ordinal);
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
    public void Evaluate_LabeledContinueCrossingDriverLoop_DeclinesWithLabelControlFlow()
    {
        // A labeled continue that re-enters an outer loop from inside an enclosing for-of driver
        // loop would leak the inner iterator (the VM continue path performs no driver cleanup), so
        // it must decline before VM execution to preserve no-mixed-execution.
        var plan = GetFunctionPlan("""
            function labeled(outer, inner) {
                var total = 0;
                outerLabel: for (var x of outer) {
                    for (var y of inner) {
                        if (y === 1) {
                            continue outerLabel;
                        }

                        total = total + 1;
                    }
                }

                return total;
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
    public void Evaluate_LabeledBreakCrossingDriverLoop_DeclinesWithLabelControlFlow()
    {
        // A labeled break that exits an enclosing for-of driver loop it is not directly targeting
        // (here: break outerLabel from inside the inner for-of) would leave the inner iterator
        // active, because the VM's single-level driver cleanup only closes the driver whose break
        // target equals the jump target. Decline before VM execution to preserve no-mixed-execution.
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

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.LabelControlFlow, result.Code);
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
    public void Evaluate_UnsupportedDestructuringDriverShapes_DeclineWithExplicitReason(
        string source,
        string functionName)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.DestructuringDependency, result.Code);
        Assert.Contains("destructuring", result.Reason, StringComparison.OrdinalIgnoreCase);
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
    [InlineData(
        """
        function readObjectNested(source) {
            var { a: { b } } = source;
            return b;
        }
        """,
        "readObjectNested")]
    public void Evaluate_UnsupportedObjectDestructuringShapes_DeclineWithExplicitReason(
        string source,
        string functionName)
    {
        var plan = GetFunctionPlan(source, functionName);

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.DestructuringDependency, result.Code);
        Assert.Contains("destructuring", result.Reason, StringComparison.OrdinalIgnoreCase);
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

    // ── AC-5 negative fallback proof (#2678): unsupported async-driver sub-shapes ──
    // The TDZ-head admit must not leak the async-iterator kind or awaited driver
    // sources (Slices B/C). These exercise the decline arms directly because async
    // drivers live inside async functions, which decline before plan inspection.

    [Fact]
    public void IsSupportedIteratorInit_AsyncKind_Declines()
    {
        var instruction = new IteratorInitInstruction(
            IteratorDriverKind.Await,
            Symbol.Synthetic("__iter_state"),
            IteratorSlotIndex: 0,
            Next: -1,
            IterableProgram: ExpressionProgram.Empty);

        Assert.False(UnifiedBytecodeProductionEligibility.IsSupportedIteratorInit(instruction, out var reason));
        Assert.Contains("Async iterator driver state", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void IsSupportedIteratorInit_AwaitedSource_Declines()
    {
        var instruction = new IteratorInitInstruction(
            IteratorDriverKind.Sync,
            Symbol.Synthetic("__iter_state"),
            IteratorSlotIndex: 0,
            Next: -1,
            IterableProgram: ExpressionProgram.Empty,
            AwaitedProgram: ExpressionProgram.Empty);

        Assert.False(UnifiedBytecodeProductionEligibility.IsSupportedIteratorInit(instruction, out var reason));
        Assert.Contains("synchronous expression bytecode", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void IsSupportedForInInit_AwaitedSource_Declines()
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
        Assert.Contains("synchronous expression bytecode", reason, StringComparison.Ordinal);
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
    public void Evaluate_TypeOfUnresolvedIdentifier_DeclinesWithDynamicLookupDependency()
    {
        var plan = GetFunctionPlan("""
            function kind() {
                return typeof missing;
            }
            """,
            "kind");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency, result.Code);
        Assert.Contains("typeof identifier 'missing'", result.Reason, StringComparison.Ordinal);
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
            { "equal", "==", (int)BinaryOperator.Equal },
            { "strictEqual", "===", (int)BinaryOperator.StrictEqual },
            { "strictNotEqual", "!==", (int)BinaryOperator.StrictNotEqual },
            { "lessThan", "<", (int)BinaryOperator.LessThan },
            { "lessThanOrEqual", "<=", (int)BinaryOperator.LessThanOrEqual },
            { "greaterThan", ">", (int)BinaryOperator.GreaterThan },
            { "greaterThanOrEqual", ">=", (int)BinaryOperator.GreaterThanOrEqual }
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
    public void Evaluate_ClassMethodWithSuperProperty_DeclinesSuperPropertyDependency()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                get value() { return 1; }
            }

            class Child extends Base {
                readSuper() {
                    return super.value;
                }
            }
            """,
            "Child",
            "readSuper");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.SuperPropertyDependency, result.Code);
    }
}
