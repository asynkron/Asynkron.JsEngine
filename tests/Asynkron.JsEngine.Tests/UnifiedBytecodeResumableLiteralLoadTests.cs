using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for the regex-literal leaf load (<c>/ab+c/g</c>,
///     <see cref="UnifiedBytecodeOpCode.LoadRegexLiteral" />) inside the resumable VM
///     (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />). Before this slice the resumable
///     opcode allowlist (<c>TryFindUnsupportedResumableOpcode</c>) omitted <c>LoadRegexLiteral</c>, so any
///     generator/async body containing a regex literal fell back to the interpreter.
///
///     The opcode is a pure leaf push: it allocates a FRESH RegExp from the program's interned
///     pattern/flags constants and pushes it, carries no <c>AwaitedProgram</c>, and reads nothing off the
///     operand stack — so it always runs to completion inside one resumable step and never participates in
///     suspension/resume operand-stack restoration. The resumable handler reuses the sync VM's
///     <c>RegExpHelper.CreateRegExpLiteral</c> verbatim, allocating a distinct object per evaluation per the
///     JS regex-literal spec.
///
///     (<c>LoadTemplateObject</c> is deliberately left out of this slice: it is emitted only inside a
///     tagged-template CALL, whose call boundary the resumable route still declines, so the opcode is
///     unreachable and cannot be verified end-to-end here. The NEGATIVE gate below pins that <c>new.target</c>
///     also stays declined.)
///
///     Each proof asserts (a) ROUTING — eligibility via <c>EvaluateResumable</c> plus, for the end-to-end
///     runs, the resumable fast-path log (a fall-back to the interpreter fails the test) — and (b)
///     correctness for the adversarial cases: a regex literal whose match state is observed across a yield,
///     a regex literal allocating a new object per loop iteration, and the async-await variant.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableLiteralLoadTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // The gate: a generator that yields a regex literal is now admitted, and the admitted program actually
    // carries LoadRegexLiteral (proving the slice expanded eligibility to the regex-literal tier rather than
    // admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_RegexLiteralAcrossYield_AdmitsLoadRegexLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield /ab+c/gi;
                yield 2;
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

    // NEGATIVE gate: a tagged template (`tag`...``) stays declined on the resumable route. Its
    // LoadTemplateObject opcode is reachable only via the tagged-template CALL boundary, which the resumable
    // route still declines — so admitting LoadTemplateObject in isolation would have been an unverifiable
    // no-op. This pins that the regex-literal slice did NOT accidentally admit the tagged-template shape.
    [Fact]
    public void EvaluateResumable_TaggedTemplateInGenerator_StaysDeclined()
    {
        var plan = GetFunctionPlan("""
            function* g(tag, v) {
                yield tag`a${v}b`;
                yield 2;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
    }

    // NEGATIVE gate: a generator that reads `new.target` (LoadNewTarget) stays declined — that opcode has
    // no resumable handler and is intentionally absent from the resumable allowlist (the resumable loop
    // carries no newTarget value). Pins the admitted surface so a future change cannot silently route an
    // unverified leaf load through the resumable path on the back of this slice.
    [Fact]
    public void EvaluateResumable_NewTargetInGenerator_StaysDeclined()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 1;
                yield new.target;
                yield 2;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
    }

    // End-to-end: a generator yields a regex literal across a suspension; the matched result and the
    // regex's own flags/lastIndex are observed correctly on resume.
    [Fact(Timeout = 5000)]
    public async Task GeneratorRegexLiteralAcrossYield_RoutesResumableAndMatches()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                var re = /a(b+)c/;
                yield 1;
                var m = re.exec("xxabbbcyy");
                yield m[1];          // "bbb"
                yield re.source;     // "a(b+)c"
            }

            var it = g();
            var a = it.next().value;   // 1
            var b = it.next().value;   // "bbb"
            var c = it.next().value;   // "a(b+)c"
            a + "|" + b + "|" + c;
            """);

        Assert.Equal("1|bbb|a(b+)c", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial: a regex literal allocates a FRESH object every time its source expression evaluates. A
    // generator that yields the literal on each iteration of a loop must yield DISTINCT objects whose state
    // (the global `g` flag's lastIndex) does not leak between iterations — proving the resumable handler
    // mirrors the sync per-evaluation allocation, not a cached singleton.
    [Fact(Timeout = 5000)]
    public async Task GeneratorRegexLiteralPerIteration_AllocatesFreshObjects()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(n) {
                var i = 0;
                while (i < n) {
                    yield /x/g;        // fresh regex literal each iteration
                    i++;
                }
            }

            var it = g(2);
            var r1 = it.next().value;
            var r2 = it.next().value;
            r1.lastIndex = 5;          // mutate the first object's state
            (r1 !== r2) + "|" + r1.lastIndex + "|" + r2.lastIndex;
            """);

        // distinct objects; mutating r1.lastIndex does not affect the independently-allocated r2.
        Assert.Equal("true|5|0", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // End-to-end async: an async function that constructs a regex literal AFTER an await suspension routes
    // through the resumable fast path. The regex's own properties (source/flags/lastIndex) are read back to
    // prove the freshly-pushed RegExp survived the await resume. (Property READS are admitted; a method
    // call such as `.exec(...)` is intentionally avoided here because a second-boundary member call still
    // declines on CallDependency, which would mask the regex-literal opcode under test.)
    [Fact(Timeout = 5000)]
    public async Task AsyncRegexLiteralAcrossAwait_RoutesResumableAndBuildsRegex()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(gate) {
                await gate;
                var re = /(\d+)-(\d+)/gi;
                return re.source + "|" + re.flags + "|" + re.lastIndex;
            }

            run(Promise.resolve(0)).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal("(\\d+)-(\\d+)|gi|0", result);
        AssertAsyncFastPath("run", argc: 1);
    }

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
