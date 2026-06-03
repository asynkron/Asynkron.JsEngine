using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for the resumable value-computation tier. The resumable VM
///     (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />) now handles the NON-SUSPENDING
///     value-tier opcodes — property reads (<c>GetNamedProperty</c>, <c>GetComputedProperty</c>),
///     <c>typeof</c> (<c>TypeOf</c>, <c>TypeOfIdentifier</c>) and the unary operators
///     (<c>UnaryPlus</c>, <c>UnaryMinus</c>, <c>UnaryLogicalNot</c>, <c>UnaryBitwiseNot</c>,
///     <c>UnaryVoid</c>). A generator/async whose body uses these BETWEEN suspension points is now
///     admitted by <see cref="UnifiedBytecodeProductionEligibility.EvaluateResumable" /> and produces
///     correct results at runtime.
///
///     Each proof asserts (a) eligibility/route admission — directly via EvaluateResumable for the
///     opcode-set gate, and via the resumable fast-path log for the end-to-end run — and (b) the correct
///     runtime value sequence.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableValueTierTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";

    // The value-tier gate: a generator that reads a property, applies unary minus and typeof between
    // yields is now admitted by EvaluateResumable (was previously declined on the value-tier opcodes).
    [Fact]
    public void EvaluateResumable_PropertyAndUnaryBetweenYields_AdmitsValueTierOpcodes()
    {
        var plan = GetFunctionPlan("""
            function* g(o) {
                yield o.a;
                yield -o.b;
                yield typeof o.c;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);

        // The admitted program must actually carry the newly-implemented value-tier opcodes,
        // proving the slice expanded eligibility rather than admitting an unrelated shape.
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.UnaryMinus);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.TypeOf);
    }

    // End-to-end: the same generator routes through the resumable fast path and yields the correct
    // value sequence across the suspension points.
    [Fact(Timeout = 5000)]
    public async Task GeneratorPropertyAndUnaryBetweenYields_RoutesResumableAndProducesValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                yield o.a;
                yield -o.b;
                yield typeof o.c;
            }

            var it = g({ a: 10, b: 7, c: "hi" });
            var first = it.next().value;
            var second = it.next().value;
            var third = it.next().value;
            first + "|" + second + "|" + third;
            """);

        // o.a = 10, -o.b = -7, typeof o.c = "string".
        Assert.Equal("10|-7|string", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // End-to-end: computed property reads, unary plus/not/bitwise-not and void also flow through
    // the resumable VM value-tier between yields.
    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedAndUnaryVariety_RoutesResumableAndProducesValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o, k) {
                yield +o[k];
                yield !o.flag;
                yield ~o.bits;
                yield void o.anything;
            }

            var it = g({ count: "5", flag: false, bits: 0, anything: 99 }, "count");
            var a = it.next().value;
            var b = it.next().value;
            var c = it.next().value;
            var d = it.next().value;
            "" + a + "|" + b + "|" + c + "|" + d;
            """);

        // +o["count"] = 5, !o.flag = true, ~o.bits = -1, void o.anything = undefined.
        Assert.Equal("5|true|-1|undefined", result);
        AssertGeneratorFastPath("g", argc: 2);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

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
