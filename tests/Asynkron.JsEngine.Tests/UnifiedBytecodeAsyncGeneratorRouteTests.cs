using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for the first production async-generator route through
///     <see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />. The admitted boundary is deliberately
///     narrow: simple-parameter async generators whose body is otherwise resumable-eligible can route, including
///     non-awaited <c>yield*</c> and <c>yield* await</c> over a delegated async iterable.
/// </summary>
[Category(TestCategories.AsyncRuntime)]
[Category(TestCategories.IteratorRuntime)]
public sealed class UnifiedBytecodeAsyncGeneratorRouteTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ResumableAsyncGeneratorFastPathLog =
        "unified-bytecode-resumable-async-generator-fast-path";

    [Fact]
    public void EvaluateResumable_AsyncGeneratorSimpleYield_AdmitsYield()
    {
        var plan = GetFunctionPlan("""
            async function* values(x) {
                yield x;
            }
            """,
            "values");

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
    public void EvaluateResumable_AsyncGeneratorYieldStar_AdmitsYieldStar()
    {
        var plan = GetFunctionPlan("""
            async function* relay(values) {
                yield* values;
            }
            """,
            "relay");

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
    public void EvaluateResumable_AsyncGeneratorYieldAwait_AdmitsAwaitValueAndYield()
    {
        var plan = GetFunctionPlan("""
            async function* values(source) {
                yield await source;
            }
            """,
            "values");

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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Yield);
    }

    [Fact]
    public void EvaluateResumable_AsyncGeneratorForLetAwait_AdmitsPerIterationReplacementScope()
    {
        var plan = GetFunctionPlan("""
            async function* callAsync(iterations, pushAwait) {
                for (let i = 0; i < iterations; i++) {
                    await pushAwait();
                }
                return 0;
            }
            """,
            "callAsync");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true, IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PushEnvironment);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.AwaitAndDiscard);
    }

    [Fact]
    public void EvaluateResumable_AsyncGeneratorYieldStarAwait_AdmitsAwaitValueAndYieldStar()
    {
        var plan = GetFunctionPlan("""
            async function* relay(values) {
                yield* await values;
            }
            """,
            "relay");

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

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorDirectNext_RoutesResumableAndSettlesIteratorResults()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;

            async function* values(x) {
                yield x;
                yield x + 1;
                return x + 2;
            }

            async function run() {
                var iterator = values(4);
                var first = await iterator.next();
                var second = await iterator.next();
                var third = await iterator.next();
                return first.value + ":" + first.done + "|" +
                    second.value + ":" + second.done + "|" +
                    third.value + ":" + third.done;
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal("4:false|5:false|6:true", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=values argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorYieldAwait_RoutesResumableAndYieldsSettledValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;

            async function* values(source) {
                yield await source;
                return "done";
            }

            async function run() {
                var iterator = values(Promise.resolve(7));
                var first = await iterator.next();
                var second = await iterator.next("sent");
                return first.value + ":" + first.done + "|" +
                    second.value + ":" + second.done;
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal("7:false|done:true", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=values argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorForLetAwait_RoutesResumableAndInterleavesPromiseTicks()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            var actual = [];
            var iterations = 2;

            async function pushAwait() {
                actual.push("await");
            }

            async function* callAsync() {
                for (let i = 0; i < iterations; i++) {
                    await pushAwait();
                }
                return 0;
            }

            callAsync().next();

            new Promise(function(resolve) {
                actual.push(1);
                resolve();
            }).then(function() {
                actual.push(2);
            }).then(function() {
                output = actual.join("|");
            });

            output;
            """);

        Assert.Equal("await|1|await|2", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=callAsync argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorObjectSpreadWithYieldSource_RoutesResumableAndPreservesSpreadOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var s = Symbol("s");

            async function* values() {
                yield {
                    ...yield yield,
                    ...(function(arg) {
                        var yield = arg;
                        return {...yield};
                    }(yield)),
                    ...yield
                };
            }

            async function run() {
                var iterator = values();
                await iterator.next();
                await iterator.next();
                await iterator.next({ x: 10, a: 0, b: 0, [s]: 1 });
                await iterator.next({ y: 20, a: 1, b: 1, [s]: 42 });
                var item = await iterator.next({ z: 30, b: 2 });
                return item.done + ":" +
                    item.value.x + ":" +
                    item.value.y + ":" +
                    item.value.z + ":" +
                    item.value.a + ":" +
                    item.value.b + ":" +
                    item.value[s] + ":" +
                    Object.keys(item.value).length;
            }

            var output = undefined;
            run().then(value => output = value);
            output;
            """);

        Assert.Equal("false:10:20:30:1:2:42:5", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=values argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorNoParameterArgumentsRead_RoutesResumableAndReadsCallArguments()
    {
        var plan = GetFunctionPlan("""
            async function* values() {
                yield arguments.length;
                yield arguments[0];
            }
            """,
            "values");
        var eligibility = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                IsAsyncLike: true,
                IsGenerator: true,
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true,
                AllowsImplicitArgumentsObjectPropertyReadOperands: true));
        Assert.True(eligibility.IsEligible, eligibility.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, eligibility.Code);

        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;

            async function* values() {
                yield arguments.length;
                yield arguments[0];
            }

            async function run() {
                var iterator = values(42, 43);
                var first = await iterator.next();
                var second = await iterator.next();
                return first.value + ":" + second.value;
            }

            run().then(value => output = value, error => output = error.name + ":" + error.message);
            output;
            """);

        Assert.Equal("2:42", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=values argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorYieldStar_RoutesResumableAndSettlesDelegatedAsyncIterator()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;

            async function* relay(values) {
                return yield* values;
            }

            var calls = [];
            var delegated = {
                [Symbol.asyncIterator]() {
                    var index = 0;
                    return {
                        async next(value) {
                            calls.push("next:" + String(value));
                            if (index === 0) {
                                index = 1;
                                return { value: "first", done: false };
                            }
                            if (index === 1) {
                                index = 2;
                                return { value: "second:" + value, done: false };
                            }
                            return { value: "done:" + value, done: true };
                        }
                    };
                }
            }

            async function run() {
                var iterator = relay(delegated);
                var first = await iterator.next("ignored");
                var second = await iterator.next("sent");
                var third = await iterator.next("final");
                return first.value + ":" + first.done + "|" +
                    second.value + ":" + second.done + "|" +
                    third.value + ":" + third.done + "|" +
                    calls.join(",");
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal("first:false|second:sent:false|done:final:true|next:undefined,next:sent,next:final", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=relay",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorYieldStarAwait_RoutesResumableAndSettlesAwaitedSource()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;

            async function* relay(values) {
                return yield* await values;
            }

            var calls = [];
            var delegated = {
                [Symbol.asyncIterator]() {
                    var index = 0;
                    return {
                        async next(value) {
                            calls.push("next:" + String(value));
                            if (index === 0) {
                                index = 1;
                                return { value: "first", done: false };
                            }
                            if (index === 1) {
                                index = 2;
                                return { value: "second:" + value, done: false };
                            }
                            return { value: "done:" + value, done: true };
                        }
                    };
                }
            };

            async function run() {
                var iterator = relay(Promise.resolve(delegated));
                var first = await iterator.next("ignored");
                var second = await iterator.next("sent");
                var third = await iterator.next("final");
                return first.value + ":" + first.done + "|" +
                    second.value + ":" + second.done + "|" +
                    third.value + ":" + third.done + "|" +
                    calls.join(",");
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal("first:false|second:sent:false|done:final:true|next:undefined,next:sent,next:final", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=relay",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorTryFinallyCleanup_RoutesResumableAndRunsCleanup()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            let log = [];

            async function* gen() {
                try {
                    log.push("try-start");
                    yield "body";
                    log.push("try-end");
                } finally {
                    log.push("finally-start");
                    log.push("finally-end");
                }
            }

            async function run() {
                var iterator = gen();
                var first = await iterator.next();
                var second = await iterator.next();
                return first.value + ":" + first.done + "|" +
                    String(second.value) + ":" + second.done + "|" +
                    log.join("|");
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal(
            "body:false|undefined:true|try-start|try-end|finally-start|finally-end",
            result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=gen argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorDeclarationFreeDirectEval_RoutesResumableAndSettles()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            var externalValue = 20;

            async function* values(delta) {
                eval("'non-injecting direct eval'");
                externalValue = externalValue + delta;
                yield externalValue + 1;
            }

            async function run() {
                var iterator = values(21);
                var first = await iterator.next();
                return first.value + ":" + externalValue;
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal("42:41", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=values argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorDefaultParameter_BindsParametersAndRoutesResumable()
    {
        await using var engine = CreateEngine();

        // Eager IteratorBindingInitialization binds the defaulted parameter x = 7 at call
        // time (no argument supplied → default applies) and the async-generator body routes
        // through the resumable VM, so run() resolves with the yielded value.
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;

            async function* values(x = 7) {
                yield x;
            }

            async function run() {
                var iterator = values();
                var first = await iterator.next();
                return first.value + ":" + first.done;
            }

            run().then(
                value => output = "resolved:" + value,
                error => output = "rejected:" + String(error));
            output;
            """);

        Assert.Equal("resolved:7:false", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=values argc=0",
                StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains("async-generator-runner-fallback", StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorNonSimpleParameter_BindsParametersAndRoutesResumable()
    {
        await using var engine = CreateEngine();

        // The default-value expression (events.push("default"), 7) evaluates eagerly at call
        // time during IteratorBindingInitialization, so "default" is recorded before the body
        // runs. The body then routes through the resumable VM, pushing "body:7" and yielding 7.
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            var events = [];

            async function* values(x = (events.push("default"), 7)) {
                events.push("body:" + x);
                yield x;
            }

            async function run() {
                var iterator = values();
                var first = await iterator.next();
                return first.value + ":" + first.done + "|" + events.join(",");
            }

            run().then(
                value => output = "resolved:" + value,
                error => output = "rejected:" + events.join(",") + "|" + String(error));
            output;
            """);

        Assert.Equal("resolved:7:false|default,body:7", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=values argc=0",
                StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains("async-generator-runner-fallback", StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorDestructuringParameter_BindsParametersAndRoutesResumable()
    {
        await using var engine = CreateEngine();

        // Eager IteratorBindingInitialization binds the array pattern [x] at call time
        // (x = 1 from the supplied [1]) and the async-generator body routes through the
        // resumable VM, so run() resolves with the yielded value.
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;

            async function* values([x]) {
                yield x;
            }

            async function run() {
                var iterator = values([1]);
                var first = await iterator.next();
                return first.value + ":" + first.done;
            }

            run().then(
                value => output = "resolved:" + value,
                error => output = "rejected:" + String(error));
            output;
            """);

        Assert.Equal("resolved:1:false", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=values argc=1",
                StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains("async-generator-runner-fallback", StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorCapturedHoistedHelper_RoutesResumableAndSettles()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;

            async function* values(x) {
                function addOne() {
                    return x + 1;
                }

                yield addOne();
            }

            async function run() {
                var iterator = values(4);
                var first = await iterator.next();
                var second = await iterator.next();
                return first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal("5:false|undefined:true", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=values argc=1",
                StringComparison.Ordinal));
    }

    [Fact]
    public void SourceGate_AcceptedAsyncGeneratorRoute_DoesNotDelegateToRunnerOrExpressionEvaluation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var asyncGeneratorInvokerPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Ast",
            "TypedAstEvaluator.AsyncGeneratorInvoker.cs");
        var virtualMachinePath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "UnifiedBytecode",
            "UnifiedBytecodeVirtualMachine.cs");

        var acceptedStepBody = ExtractSourceBetween(
            File.ReadAllText(asyncGeneratorInvokerPath),
            "private AsyncGeneratorStepResult ExecuteUnifiedBytecodeStep",
            "private static UnifiedBytecodeResumeMode ToUnifiedResumeMode");
        var virtualMachineSource = File.ReadAllText(virtualMachinePath);
        var forbiddenAcceptedStepTokens = new[]
        {
            "_inner",
            "ExecuteAsyncStep(",
            "ExecuteClassifiedAsyncDeclinedBodyStep(",
            "new ExecutionPlanRunner(",
            "EvaluateDynamicExpressionProgram(",
            "EvaluateStandaloneExpressionProgram(",
            "EvaluateLegacyAstExpression",
            "ProfileEvaluateExpression"
        };
        var forbiddenVirtualMachineTokens = new[]
        {
            "ExecutionPlanRunner",
            "ExpressionProgram",
            "EvaluateDynamicExpressionProgram(",
            "EvaluateStandaloneExpressionProgram(",
            "EvaluateLegacyAstExpression",
            "ProfileEvaluateExpression"
        };

        AssertNoForbiddenTokens(
            acceptedStepBody,
            forbiddenAcceptedStepTokens,
            "Accepted async-generator resumable steps must stay on the unified VM route.");
        AssertNoForbiddenTokens(
            virtualMachineSource,
            forbiddenVirtualMachineTokens,
            "The unified bytecode VM must not delegate back into runner or expression-evaluation bridges.");
    }

    [Fact]
    public void SourceGate_AsyncGeneratorInvoker_DoesNotKeepDeclinedBodyRunnerFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var asyncGeneratorInvokerPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Ast",
            "TypedAstEvaluator.AsyncGeneratorInvoker.cs");

        var source = File.ReadAllText(asyncGeneratorInvokerPath);
        var forbiddenFallbackTokens = new[]
        {
            "_fallbackRunner",
            "ExecuteFallbackRunnerStep",
            "ExecuteAsyncStep(",
            "ExecuteClassifiedAsyncDeclinedBodyStep(",
            "async-generator-runner-fallback",
            "ExecutionPlanRunner.ResumeMode",
            "CreateClassifiedAsyncGeneratorDeclinedBodyRunner"
        };

        AssertNoForbiddenTokens(
            source,
            forbiddenFallbackTokens,
            "E6 requires AsyncGeneratorInvoker declined bodies to fail explicitly instead of constructing a renamed runner fallback.");
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

    private static void AssertNoForbiddenTokens(
        string source,
        IReadOnlyList<string> forbiddenTokens,
        string message)
    {
        var matches = forbiddenTokens
            .Where(token => source.Contains(token, StringComparison.Ordinal))
            .ToArray();

        Assert.True(matches.Length == 0, $"{message}\nForbidden tokens: {string.Join(", ", matches)}");
    }

    private static string ExtractSourceBetween(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing source start marker: {startMarker}");

        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing source end marker: {endMarker}");

        return source[start..end];
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Asynkron.JsEngine.sln")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
