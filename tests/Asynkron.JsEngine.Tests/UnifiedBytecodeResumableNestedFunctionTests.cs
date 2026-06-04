using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Boundary pins for the resumable nested-function tier (B23 nested function LITERAL + B36 hoisted nested
///     FUNCTION DECLARATION inside a generator/async body). Both shapes are INVESTIGATED-DECLINED: they stay on
///     the IR runner (correct value) and must NOT route through the resumable VM fast path. These pins protect
///     the boundary against a future naive opcode admission that would silently corrupt results.
///
///     Why declined (architecture, not a missing handler):
///
///     B23 — a nested function literal that closes over the resumable VM closes over the activation's
///     environment, but the generator/async body's own locals (`let`/`var`/params) are realised as FLAT SLOTS
///     on the resume state, not as environment bindings. A nested function that CAPTURES such a local cannot see
///     it through its environment chain (`function* g(){ var n=1; var f=()=&gt;n; yield f(); }` would throw
///     `ReferenceError: n is not defined` on a naive resumable route), and a sync-on-create would still go stale
///     on a post-capture mutation (`n=2`) because the body mutates the slot, not the env. The non-capturing
///     subset happens to work, but it cannot be separated from the broken capturing subset without new
///     free-variable capture analysis that the resumable route does not carry. Declining the whole tier keeps
///     every shape correct on the runner.
///
///     B36 — a hoisted nested function declaration is materialised into its binding by
///     FunctionDeclarationInstantiation at call time. The resumable generator/async setup only copies parameters
///     into positional slots and TDZ-inits lexical slots; it does NOT run hoisted-function instantiation, so the
///     declared name's slot stays `undefined` and a call (`yield helper()`) yields `undefined` / throws on a
///     naive resumable route. Correct admission needs hoisted-declaration slot population threaded into all three
///     resumable invokers — separate infrastructure, not a 1:1 opcode admission.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableNestedFunctionTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // B23: a generator that defines and calls a nested function LITERAL is correct on the runner and stays off
    // the resumable fast path.
    [Fact(Timeout = 5000)]
    public async Task GeneratorNestedFunctionLiteral_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){ var h = function(a){ return a*2; }; yield h(3); }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(6d, result);
        AssertGeneratorNotRouted();
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

    // B36: a generator with a hoisted nested function DECLARATION is correct on the runner (5) and stays off the
    // resumable fast path.
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

    // B36 hoisting proof: a CALL textually BEFORE the declaration still works on the runner (7); no routing.
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

    // B23 async variant: an async function defines and calls a nested function literal around an await. Correct
    // on the runner (14); stays off the resumable fast path.
    [Fact(Timeout = 5000)]
    public async Task AsyncNestedFunctionLiteral_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function run(){
                var dbl = function(a){ return a*2; };
                var x = dbl(3);
                await Promise.resolve(0);
                return x + dbl(4);
            }
            run().then(v => done = v);
            done;
            """);

        Assert.Equal(14d, result);
        AssertAsyncNotRouted();
    }

    // Eligibility gate (the eligibility layer admits the program shape, but the resumable ROUTE still declines
    // because the nested-function tier is not soundly routable — this pins that the decline is enforced at the
    // route, keeping the boundary explicit). The function-literal shape compiles to a program containing the
    // LoadFunctionLiteral opcode, which remains OFF the resumable allowlist.
    [Fact]
    public void EvaluateResumable_NestedFunctionLiteral_DeclinesOnLoadFunctionLiteralOpcode()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){ var h = function(a){ return a*2; }; yield h(3); }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains("LoadFunctionLiteral", result.Reason, StringComparison.Ordinal);
    }

    // Eligibility gate: the hoisted function declaration shape declines at the resumable instruction gate (the
    // FunctionDeclarationInstruction is not on the resumable instruction allowlist).
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
    }

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
