using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for burn-down item B22 — regex literal (<c>/pat/flags</c>) inside a resumable body.
///     The resumable VM (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />) now handles the
///     non-suspending <c>LoadRegexLiteral</c> opcode, so a generator/async whose body materializes a
///     regex literal BETWEEN suspension points is admitted by
///     <see cref="UnifiedBytecodeProductionEligibility.EvaluateResumable" /> and produces correct results.
///
///     Each proof asserts (a) route admission — directly via EvaluateResumable for the opcode-set gate,
///     and via the resumable generator fast-path log for the end-to-end run so an interpreter fallback
///     fails the test — and (b) the correct runtime behavior, including the per-evaluation freshness
///     (<c>lastIndex</c>) edge case the spec mandates.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableRegexLiteralTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";

    // The gate: a generator that yields the match result of a regex literal between yields is now
    // admitted by EvaluateResumable, and the admitted program actually carries LoadRegexLiteral —
    // proving the slice expanded eligibility rather than admitting an unrelated shape.
    [Fact]
    public void EvaluateResumable_RegexLiteralBetweenYields_AdmitsLoadRegexLiteralOpcode()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield /\d+/;
                yield /[a-z]+/g;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadRegexLiteral);
    }

    // End-to-end: the same generator routes through the resumable fast path and yields the correct
    // values across the suspension points.
    [Fact(Timeout = 5000)]
    public async Task GeneratorRegexLiteralBetweenYields_RoutesResumableAndProducesValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield /\d+/;
                yield /[a-z]+/g;
                yield /X/i;
            }

            // The generator BODY only loads regex literals and yields them — the matching/property
            // inspection happens OUTSIDE the resumable body so this proves the LoadRegexLiteral opcode
            // alone, not unrelated property-read / call dispatch shapes.
            var it = g();
            var first = it.next().value;
            var second = it.next().value;
            var third = it.next().value;
            first.source + "|" + first.test("a7b") + "|" +
                second.source + "|" + second.flags + "|" + third.flags;
            """);

        // /\d+/.source = "\d+", test("a7b") = true; /[a-z]+/g.source = "[a-z]+", flags = "g"; /X/i.flags = "i".
        Assert.Equal("\\d+|true|[a-z]+|g|i", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial freshness edge case: each evaluation of a regex literal must produce a DISTINCT
    // RegExp object with lastIndex reset to 0. A naive implementation that cached one instance across
    // resumes (or shared it with another evaluation) would leak a non-zero lastIndex from a prior
    // stateful match and corrupt the next .exec() against the global flag. The resumable handler builds
    // a fresh instance per LoadRegexLiteral execution, identical to the sync VM, so re-entering the
    // same source line after a suspension still starts from lastIndex 0.
    [Fact(Timeout = 5000)]
    public async Task GeneratorRegexLiteralAcrossResume_ProducesFreshInstancePerEvaluation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(s) {
                // Each iteration re-evaluates the same global regex literal. Because a fresh instance
                // is created every time, lastIndex always starts at 0 and the first match is returned.
                while (true) {
                    var re = /\d/g;
                    re.exec(s);
                    yield re.lastIndex;
                }
            }

            var it = g("a1b2");
            var a = it.next().value;
            var b = it.next().value;
            var c = it.next().value;
            "" + a + "|" + b + "|" + c;
            """);

        // Fresh /\d/g each iteration: exec advances lastIndex to 2 (first digit at index 1) every time.
        Assert.Equal("2|2|2", result);
        AssertGeneratorFastPath("g", argc: 1);
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
