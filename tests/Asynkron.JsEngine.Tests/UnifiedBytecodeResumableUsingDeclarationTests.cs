using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for direct function-body <c>using</c> declarations on the resumable unified-bytecode
///     route. The resource registers through <see cref="UnifiedBytecodeOpCode.RegisterDisposable" /> against
///     the materialized resumable body environment and is disposed when the frame completes or throws. Direct
///     async-function <c>await using</c> also awaits async-dispose settlement before resolving or rejecting.
///     Block-scoped <c>using</c> remains declined until the resumable VM owns a persisted environment stack.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableUsingDeclarationTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public void EvaluateResumable_FunctionBodyUsing_AdmitsRegisterDisposable()
    {
        var plan = GetFunctionPlan("""
            function* g(resource) {
                using value = resource;
                yield "body";
                return "done";
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.RegisterDisposable);
    }

    [Fact]
    public void EvaluateResumable_AsyncFunctionBodyAwaitUsing_AdmitsAsyncDisposableRegistration()
    {
        var plan = GetFunctionPlan("""
            async function run(resource) {
                await Promise.resolve(0);
                await using value = resource;
                return "done";
            }
            """,
            "run");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.RegisterDisposable &&
                                  instruction.Operand == 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorFunctionBodyUsing_DisposesOnCompletionAndRoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var log = [];
            function* g(resource) {
                using value = resource;
                log.push("body");
                yield "yielded";
                log.push("after");
                return "done";
            }

            var it = g({ [Symbol.dispose]() { log.push("disposed"); } });
            var first = it.next();
            log.push("first:" + first.value + ":" + first.done);
            var second = it.next();
            log.push("second:" + second.value + ":" + second.done);
            log.join(",");
            """);

        Assert.Equal("body,first:yielded:false,after,disposed,second:done:true", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorFunctionBodyUsing_NonObjectThrowsOnResumableRoute()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(value) {
                using resource = value;
                yield 1;
            }

            var it = g(1);
            var caught = "none";
            try {
                it.next();
            } catch (e) {
                caught = e.name;
            }

            caught;
            """);

        Assert.Equal("TypeError", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncFunctionBodyUsing_DisposesBeforeResolutionAndRoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var log = [];
            async function run(resource) {
                await Promise.resolve(0);
                using value = resource;
                log.push("body");
                return "done";
            }

            var final = "";
            run({ [Symbol.dispose]() { log.push("disposed"); } })
                .then(value => {
                    log.push("return:" + value);
                    final = log.join(",");
                });
            final;
            """);

        Assert.Equal("body,disposed,return:done", result);
        AssertAsyncFastPath("run", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncFunctionBodyAwaitUsing_AwaitsAsyncDisposeBeforeResolutionAndRoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var log = [];
            async function run(resource) {
                await Promise.resolve(0);
                await using value = resource;
                log.push("body");
                return "done";
            }

            var final = "";
            run({
                [Symbol.asyncDispose]() {
                    log.push("dispose-start");
                    return Promise.resolve().then(() => log.push("dispose-done"));
                }
            }).then(value => {
                log.push("return:" + value);
                final = log.join(",");
            });
            final;
            """);

        Assert.Equal("body,dispose-start,dispose-done,return:done", result);
        AssertAsyncFastPath("run", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncFunctionBodyAwaitUsing_RejectedAsyncDisposeRejectsFunctionPromise()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var log = [];
            async function run(resource) {
                await using value = resource;
                log.push("body");
                return "done";
            }

            var final = "";
            run({
                [Symbol.asyncDispose]() {
                    log.push("dispose-start");
                    return Promise.reject("dispose-fail");
                }
            }).then(
                value => { final = "resolved:" + value; },
                error => {
                    log.push("reject:" + error);
                    final = log.join(",");
                });
            final;
            """);

        Assert.Equal("body,dispose-start,reject:dispose-fail", result);
        AssertAsyncFastPath("run", argc: 1);
    }

    [Fact]
    public void EvaluateResumable_BlockScopedUsing_StaysDeclined()
    {
        var plan = GetFunctionPlan("""
            function* g(resource) {
                {
                    using value = resource;
                    yield "body";
                }
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("function-body using", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_BlockScopedAwaitUsing_StaysDeclined()
    {
        var plan = GetFunctionPlan("""
            async function run(resource) {
                {
                    await using value = resource;
                    await Promise.resolve(0);
                }
            }
            """,
            "run");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("function-body using", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_AsyncGeneratorBodyAwaitUsing_StaysDeclined()
    {
        var plan = GetFunctionPlan("""
            async function* g(resource) {
                await using value = resource;
                yield "body";
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true, IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("direct async function bodies", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_BlockScopedUsingShadowingParameter_StaysDeclined()
    {
        var plan = GetFunctionPlan("""
            function* g(value, resource) {
                {
                    using value = resource;
                    yield "body";
                }
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("function-body using", result.Reason, StringComparison.Ordinal);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"unified-bytecode-resumable-generator-fast-path func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private void AssertAsyncFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"unified-bytecode-resumable-async-fast-path func={functionName} argc={argc}",
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
