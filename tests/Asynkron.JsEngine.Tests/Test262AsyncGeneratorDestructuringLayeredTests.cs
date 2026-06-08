using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.AsyncRuntime)]
[Category(TestCategories.IteratorRuntime)]
public sealed class Test262AsyncGeneratorDestructuringLayeredTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    [Fact]
    public void Layer1_ClassAsyncGeneratorDestructuringMethod_ParsesAndLowersMethodBody()
    {
        var plan = GetClassMethodPlan("""
            class C {
                async *method([x, y, z]) {
                    yield x + y + z;
                }
            }
            """,
            "C",
            "method");

        Assert.NotEmpty(plan.Instructions);
    }

    [Fact(Timeout = 5000)]
    public async Task Layer2_SyncGeneratorClassDestructuringMethod_DeclinesExplicitly()
    {
        await using var engine = CreateEngine();
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => engine.Evaluate("""
            var callCount = 0;
            class C {
                *method([x = 23] = []) {
                    callCount += x;
                }
            }

            var step = new C().method().next();
            ({ callCount, done: step.done });
            """));

        Assert.StartsWith(
            "Sync-generator body '<anonymous>' is not eligible for unified bytecode execution:",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("Non-simple sync-generator parameter lists", exception.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task Layer3_AsyncGeneratorClassDestructuringMethod_DeclinesUnifiedRouteThenFailsExplicitly()
    {
        await using var engine = CreateEngine();
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => engine.EvaluateAndAwait("""
            var callCount = 0;
            var output = undefined;
            class C {
                async *method([x = 23] = []) {
                    callCount += x;
                }
            }

            new C().method().next().then(
                value => output = "resolved:" + callCount,
                error => output = "rejected:" + String(error));
            output;
            """));

        Assert.Contains("Non-simple async-generator parameter lists", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains("async-generator-runner-fallback", StringComparison.Ordinal));
    }

    [Fact]
    public void Layer4_AsyncGeneratorForAwaitDestructuringBody_LowersThenDeclinesAtResumableEligibility()
    {
        var plan = GetFunctionPlan("""
            async function *fn() {
                for await (const [x, y, z] of [[1, 2, 3]]) {
                    yield x + y + z;
                }
            }
            """,
            "fn");

        Assert.Contains(
            plan.Instructions,
            static instruction => instruction is BindingVariableDeclarationInstruction);

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true, IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains(nameof(BindingVariableDeclarationInstruction), result.Reason, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task Layer5_AsyncFunctionForAwaitDestructuringBody_RejectsExplicitUnifiedBytecodeDecline()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            async function run() {
                var iterCount = 0;
                for await (const [x, y, z] of [[1, 2, 3]]) {
                    if (x !== 1 || y !== 2 || z !== 3) {
                        throw new Error("bad destructuring");
                    }

                    iterCount += 1;
                }

                return iterCount;
            }

            run().then(
                value => output = 'fulfilled:' + value,
                error => output = String(error));
            output;
            """);

        AssertAsyncFunctionDeclined(result, "run");
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

    private static ExecutionPlan GetClassMethodPlan(string source, string className, string methodName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<ClassDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is ClassDeclaration c && c.Name.Name == className));
        var member = Assert.Single(declaration.Definition.Members.Where(candidate => candidate.Name == methodName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)member.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}
