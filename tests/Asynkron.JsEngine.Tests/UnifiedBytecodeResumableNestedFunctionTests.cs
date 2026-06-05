using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Boundary pins for the resumable nested-function tier (B23 nested function LITERAL + B36 hoisted nested
///     FUNCTION DECLARATION inside a generator/async body). Non-capturing nested functions now route through
///     the resumable VM. Generator nested literals that capture root body locals now route through a
///     materialized body environment that aliases closure captures with flat slots. Direct root hoisted
///     function declarations now route when the declared helper does not capture the resumable activation;
///     capturing declarations remain declined until B36 owns their declaration-instantiation boundary.
///
///     Why declined (architecture, not a missing handler):
///
///     B23 — a nested function literal that does not close over the resumable activation can be created with
///     the captured outer environment and stored in a flat slot. The first generator-only captured-local slice
///     materializes a body environment that mirrors root activation slots so captured closures observe slot
///     mutations across suspension. Async/async-generator captured-local literals remain declined.
///
///     B36 — direct root hoisted function declarations are materialised by the resumable invokers before
///     `ExecuteResumable` starts. The sync-generator path also admits helpers that capture root body locals
///     once it owns a materialized body environment that mirrors flat-slot state across suspension. This slice
///     is intentionally narrow: async/async-generator captured declarations, sibling/recursive declaration
///     bindings, dynamic scope, and block-declaration runtime semantics remain declined.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableNestedFunctionTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";
    private const string ResumableAsyncGeneratorFastPathLog =
        "unified-bytecode-resumable-async-generator-fast-path";

    // B23: a non-capturing nested function literal routes through the resumable fast path.
    [Fact(Timeout = 5000)]
    public async Task GeneratorNestedFunctionLiteral_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){ var h = function(a){ return a*2; }; yield h(3); }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(6d, result);
        AssertGeneratorRouted();
    }

    // B23 captured-local generator slice: the nested arrow captures a generator local and observes the slot
    // mutation after the first yield through the materialized resumable body environment.
    [Fact(Timeout = 5000)]
    public async Task GeneratorNestedArrowCapturesLocalAcrossYields_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){ var n=1; var f=()=>n; yield f(); n=2; yield f(); }
            var it = g();
            var a = it.next().value;
            var b = it.next().value;
            a + "|" + b;
            """);

        Assert.Equal("1|2", result);
        AssertGeneratorRouted();
    }

    // B36: a generator with a direct root hoisted function declaration routes once invocation setup
    // pre-populates the declaration slot.
    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclaration_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){ function helper(){ return 5; } yield helper(); }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(5d, result);
        AssertGeneratorRouted();
    }

    // B36 hoisting proof: a CALL textually BEFORE the declaration routes and observes the hoisted value.
    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclaration_CalledBeforeTextualDeclaration_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){ yield helper(); function helper(){ return 7; } }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(7d, result);
        AssertGeneratorRouted();
    }

    // B36 generator captured-helper slice: a hoisted declaration that captures a resumable body local uses
    // the generator-owned materialized body environment and observes flat-slot mutations across suspension.
    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclarationCapturesLocal_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){
                var n = 3;
                function helper(){ return n; }
                yield helper();
                n = 4;
                yield helper();
            }
            var it = g();
            var a = it.next().value;
            var b = it.next().value;
            a + "|" + b;
            """);

        Assert.Equal("3|4", result);
        AssertGeneratorRouted();
    }

    // B36 boundary: recursive helper graphs are still outside this slice, even when the result is correct
    // on the existing runner.
    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclarationRecursiveHelper_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){
                function helper(n){ return n === 0 ? 1 : n * helper(n - 1); }
                yield helper(4);
            }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(24d, result);
        AssertGeneratorNotRouted();
    }

    // B36 boundary: sibling helper declaration graphs stay declined until declaration ordering is owned by
    // the resumable route.
    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclarationSiblingHelper_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){
                function helper(){ return other() + 1; }
                function other(){ return 2; }
                yield helper();
            }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(3d, result);
        AssertGeneratorNotRouted();
    }

    // B32 boundary: external .return()/.throw() while suspended in a protected try must run the pending
    // finally. The resumable VM does not yet drive captured/free plain writes in that early-close cleanup,
    // so this shape stays on the IR runner.
    [Fact(Timeout = 5000)]
    public async Task GeneratorTryFinallyMutatesOuterBindingAfterYield_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let cleanupCalled = false;
            function* g(){
                try {
                    yield 1;
                } finally {
                    cleanupCalled = true;
                }
            }

            var it = g();
            it.next();
            var closed = it.return(99);
            cleanupCalled + "|" + closed.value + "|" + closed.done;
            """);

        Assert.Equal("true|99|true", result);
        AssertGeneratorNotRouted();
    }

    // B23 boundary: a nested arrow that reads lexical this/private names needs a materialized generator
    // body context. Keep it on the IR runner until the resumable route owns that closure context.
    [Fact(Timeout = 5000)]
    public async Task GeneratorNestedArrowUsesPrivateThis_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function() {
                class Counter {
                    #count = 0;

                    *counter() {
                        const inc = () => ++this.#count;
                        yield inc();
                        yield inc();
                        yield inc();
                    }
                }

                const iterator = new Counter().counter();
                const r1 = iterator.next().value;
                const r2 = iterator.next().value;
                const r3 = iterator.next().value;
                return r1 === 1 && r2 === 2 && r3 === 3;
            })();
            """);

        Assert.True((bool)result!);
        AssertGeneratorNotRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncHoistedFunctionDeclaration_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function run(p){
                await p;
                return helper();
                function helper(){ return 9; }
            }
            run(Promise.resolve(0)).then(v => done = v, e => done = String(e));
            done;
            """);

        Assert.Equal(9d, result);
        AssertAsyncRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorHoistedFunctionDeclaration_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function* values(){
                function helper(){ return 11; }
                yield helper();
            }
            async function run(){
                var it = values();
                var first = await it.next();
                return first.value + ":" + first.done;
            }
            run().then(v => done = v);
            done;
            """);

        Assert.Equal("11:false", result);
        AssertAsyncGeneratorRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncHoistedFunctionDeclarationCapturesLocal_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function run(p){
                var n = 12;
                await p;
                function helper(){ return n; }
                return helper();
            }
            run(Promise.resolve(0)).then(v => done = v, e => done = String(e));
            done;
            """);

        Assert.Equal(12d, result);
        AssertAsyncNotRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorHoistedFunctionDeclarationCapturesLocal_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function* values(){
                var n = 13;
                function helper(){ return n; }
                yield helper();
                n = 14;
                yield helper();
            }
            async function run(){
                var it = values();
                var a = await it.next();
                var b = await it.next();
                return a.value + "|" + b.value;
            }
            run().then(v => done = v);
            done;
            """);

        Assert.Equal("13|14", result);
        AssertAsyncGeneratorNotRouted();
    }

    // B23 async variant: an async function materializes and name-infers a non-capturing nested function
    // literal after an await and routes through the resumable fast path. Calling that literal inside a larger
    // return expression remains a separate call-boundary gate.
    [Fact(Timeout = 5000)]
    public async Task AsyncNestedFunctionLiteral_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function run(p){
                await p;
                var dbl = function(a){ return a*2; };
                return typeof dbl + "|" + dbl.name;
            }
            run(Promise.resolve(0)).then(v => done = v);
            done;
            """);

        Assert.Equal("function|dbl", result);
        AssertAsyncRouted();
    }

    // Eligibility gate: non-capturing nested function literals are now admitted and compile to LoadFunctionLiteral.
    [Fact]
    public void EvaluateResumable_NestedFunctionLiteral_AdmitsLoadFunctionLiteralOpcode()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){ var h = function(a){ return a*2; }; yield h(3); }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadFunctionLiteral);
    }

    [Fact]
    public void EvaluateResumable_AsyncNestedFunctionLiteral_AdmitsLoadFunctionLiteralOpcode()
    {
        var plan = TopLevelGeneratorPlan("""
            async function run(p){
                await p;
                var dbl = function(a){ return a*2; };
                return typeof dbl + "|" + dbl.name;
            }
            """, "run");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadFunctionLiteral);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnsureHasName);
    }

    // Eligibility gate: root hoisted function declarations admit only when the invoker has proven that it
    // can pre-populate the declaration slots. Plain plan-only checks still decline.
    [Fact]
    public void EvaluateResumable_HoistedFunctionDeclaration_DeclinesWithoutActivationProof()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){ function helper(){ return 5; } yield helper(); }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains("FunctionDeclarationInstruction", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_HoistedFunctionDeclaration_AdmitsWithActivationProof()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){ function helper(){ return 5; } yield helper(); }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                IsGenerator: true,
                AllowsRootFunctionDeclarationInstructions: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.DoesNotContain(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareFunction);
    }

    [Fact]
    public void EvaluateResumable_NestedFunctionLiteralCapturingLocal_StillDeclines()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){ var n=1; var f=()=>n; yield f(); n=2; yield f(); }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains("captures activation binding 'n'", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_NestedFunctionLiteralCapturingLocal_AdmitsWithMaterializedBodyEnvironmentProof()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){ var n=1; var f=()=>n; yield f(); n=2; yield f(); }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                IsGenerator: true,
                AllowsMaterializedBodyEnvironmentFunctionLiterals: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadFunctionLiteral);
    }

    [Fact]
    public void EvaluateResumable_NestedFunctionLiteralWithInnerDeclarationCapturingLocal_StillDeclines()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){
                var n = 1;
                var f = function(){
                    function inner(){ return n; }
                    return inner();
                };
                yield f();
            }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains("contains a function declaration", result.Reason, StringComparison.Ordinal);
    }

    private void AssertGeneratorRouted() =>
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal));

    private void AssertAsyncRouted() =>
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableAsyncFastPathLog, StringComparison.Ordinal));

    private void AssertAsyncGeneratorRouted() =>
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableAsyncGeneratorFastPathLog, StringComparison.Ordinal));

    private void AssertGeneratorNotRouted() =>
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal));

    private void AssertAsyncNotRouted() =>
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableAsyncFastPathLog, StringComparison.Ordinal));

    private void AssertAsyncGeneratorNotRouted() =>
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableAsyncGeneratorFastPathLog, StringComparison.Ordinal));

    private static ExecutionPlan TopLevelGeneratorPlan(string source, string name)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is FunctionDeclaration f && f.Name?.Name == name));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}
