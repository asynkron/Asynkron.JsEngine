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
    public async Task Layer2_SyncGeneratorClassDestructuringMethod_BindsParametersAndRoutesResumable()
    {
        await using var engine = CreateEngine();

        // Sync-generator method with a non-simple parameter list (defaulted nested array
        // pattern [x = 23] = []). Eager IteratorBindingInitialization binds at call time: the
        // missing argument triggers the [] default, then x defaults to 23. The body has no
        // yield, so the first next() runs it to completion (callCount = 23, done = true) on
        // the resumable VM.
        var result = await engine.Evaluate("""
            var callCount = 0;
            class C {
                *method([x = 23] = []) {
                    callCount += x;
                }
            }

            var step = new C().method().next();
            ({ callCount, done: step.done });
            """);

        var summary = Assert.IsType<JsObject>(result);
        Assert.Equal(23d, summary["callCount"]);
        Assert.Equal(true, summary["done"]);
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                "unified-bytecode-resumable-generator-fast-path",
                StringComparison.Ordinal) &&
                record.Message.Contains("argc=0", StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Layer3_AsyncGeneratorClassDestructuringMethod_BindsParametersAndRoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var callCount = 0;
            var output = undefined;
            class C {
                async *method([x = 23] = []) {
                    callCount += x;
                }
            }

            async function run() {
                var iterator = new C().method();
                await iterator.next();
                return callCount;
            }

            run().then(
                value => output = "resolved:" + value,
                error => output = "rejected:" + String(error));
            output;
            """);

        // Eager IteratorBindingInitialization binds [x = 23] = [] at call time (default [],
        // then x defaults to 23) and the async-generator body routes through the resumable
        // VM: the first next() runs the body to completion, so run() resolves with 23.
        Assert.Equal("resolved:23", result?.ToString());
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
    public async Task Layer5_AsyncFunctionForAwaitDestructuringBody_RuntimeStillPasses()
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

            run().then(value => output = value);
            output;
            """);

        Assert.Equal(1d, result);
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
