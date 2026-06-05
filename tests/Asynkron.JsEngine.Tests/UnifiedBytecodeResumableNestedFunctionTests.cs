using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Boundary pins for the resumable nested-function tier (B23 nested function LITERAL + B36 hoisted nested
///     FUNCTION DECLARATION inside a generator/async body). Non-capturing nested functions now route through
///     the resumable VM. Capturing nested functions remain declined until the resume state owns a materialized
///     body environment that can alias closure captures with flat slots. Hoisted declarations remain declined
///     until resumable invokers populate declaration bindings before execution.
///
///     Why declined (architecture, not a missing handler):
///
///     B23 — a nested function literal that does not close over the resumable activation can be created with
///     the captured outer environment and stored in a flat slot. A nested function that CAPTURES a body local is
///     still unsafe: the generator/async body's own locals (`let`/`var`/params) are realised as FLAT SLOTS on
///     the resume state, not as environment bindings. The eligibility layer now detects such captures and keeps
///     them on the runner.
///
///     B36 — a hoisted nested function declaration is materialised into its binding by
///     FunctionDeclarationInstantiation at call time. The resumable generator/async setup only copies parameters
///     into positional slots and TDZ-inits lexical slots; it does NOT run hoisted-function instantiation, so the
///     declared name's slot stays `undefined` and a call (`yield helper()`) throws on a naive resumable route.
///     Correct admission needs hoisted-declaration slot population threaded into all resumable invokers.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableNestedFunctionTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

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

    // B23 capturing variant: the nested arrow CAPTURES a generator local across yields. Correct on the runner
    // (1 then 2); must NOT route (a naive resumable route throws `ReferenceError: n is not defined`).
    [Fact(Timeout = 5000)]
    public async Task GeneratorNestedArrowCapturesLocalAcrossYields_CorrectButDeclinesToRunner()
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
        AssertGeneratorNotRouted();
    }

    // B36: a generator with a hoisted nested function DECLARATION is correct on the runner and stays off the
    // resumable fast path until invocation setup populates the declaration slot.
    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclaration_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){ function helper(){ return 5; } yield helper(); }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(5d, result);
        AssertGeneratorNotRouted();
    }

    // B36 hoisting proof: a CALL textually BEFORE the declaration still works on the runner; no routing.
    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclaration_CalledBeforeTextualDeclaration_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){ yield helper(); function helper(){ return 7; } }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(7d, result);
        AssertGeneratorNotRouted();
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

    // Eligibility gate: hoisted function declarations still decline at the resumable instruction gate. The
    // sync VM has a DeclareFunction handler, but the resumable route does not own declaration instantiation yet.
    [Fact]
    public void EvaluateResumable_HoistedFunctionDeclaration_Declines()
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

    private void AssertGeneratorNotRouted() =>
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal));

    private void AssertAsyncNotRouted() =>
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableAsyncFastPathLog, StringComparison.Ordinal));

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
