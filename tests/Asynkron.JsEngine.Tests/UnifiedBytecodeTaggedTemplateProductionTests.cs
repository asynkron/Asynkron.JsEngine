using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeTaggedTemplateProductionTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ProductionFastPathLog = "unified-bytecode-production-fast-path";
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    [Fact]
    public void Evaluate_IdentifierTaggedTemplate_AdmitsTemplateObjectCall()
    {
        var plan = GetFunctionPlan("""
            function render(tag, name) {
                return tag`Hello ${name}!`;
            }
            """,
            "render");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PrepareIdentifierCallTarget);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadTemplateObject);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void Evaluate_MemberTaggedTemplate_AdmitsTemplateObjectCall()
    {
        var plan = GetFunctionPlan("""
            function render(box, value) {
                return box.tag`x${value}y`;
            }
            """,
            "render");

        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadTemplateObject);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact]
    public void EvaluateResumable_GeneratorTaggedTemplate_AdmitsTemplateObjectCall()
    {
        var plan = GetFunctionPlan("""
            function* generate(tag, name) {
                yield 0;
                yield tag`Hello ${name}!`;
            }
            """,
            "generate");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadTemplateObject);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact(Timeout = 5000)]
    public async Task IdentifierTaggedTemplate_RoutesProductionAndCallsTag()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function render(tag, name) {
                return tag`Hello ${name}!`;
            }

            render(function(strings, value) {
                return strings[0] + value + strings[1] + "|" + strings.raw[0];
            }, "Bytecode");
            """);

        Assert.Equal("Hello Bytecode!|Hello ", result);
        AssertProductionFastPath("render", argc: 2);
    }

    [Fact(Timeout = 5000)]
    public async Task MemberTaggedTemplate_RoutesProductionAndPreservesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function render(box, value) {
                return box.tag`x${value}y`;
            }

            var box = {
                prefix: "P",
                tag: function(strings, value) {
                    return this.prefix + strings[0] + value + strings[1] + "|" + strings.raw[0];
                }
            };

            render(box, 7);
            """);

        Assert.Equal("Px7y|x", result);
        AssertProductionFastPath("render", argc: 2);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorTaggedTemplate_RoutesResumableAndCallsTag()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* generate(tag, name) {
                yield 1;
                yield tag`Hello ${name}!`;
            }

            var it = generate(function(strings, value) {
                return strings[0] + value + strings[1];
            }, "Resumable");
            var first = it.next().value;
            var second = it.next().value;
            first + "|" + second;
            """);

        Assert.Equal("1|Hello Resumable!", result);
        AssertGeneratorFastPath("generate", argc: 2);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorTaggedTemplateSubstitutionAcrossYield_RestoresTemplateObjectAndArgument()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* generate(tag) {
                yield tag`mid=${yield "first"}`;
            }

            var it = generate(function(strings, value) {
                return strings[0] + value + strings[1] + "|" + strings.raw[0];
            });
            var first = it.next().value;
            var second = it.next(7).value;
            first + "|" + second;
            """);

        Assert.Equal("first|mid=7|mid=", result);
        AssertGeneratorFastPath("generate", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncTaggedTemplateAfterAwait_RoutesResumableAndCallsTag()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = "PENDING";
            async function run(tag, value) {
                await Promise.resolve(0);
                return tag`done ${value}`;
            }

            run(function(strings, value) {
                return strings[0] + value + strings[1];
            }, 9).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal("done 9", result);
        AssertAsyncFastPath("run", argc: 2);
    }

    private void AssertProductionFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ProductionFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private void AssertAsyncFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func={functionName} argc={argc}",
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
