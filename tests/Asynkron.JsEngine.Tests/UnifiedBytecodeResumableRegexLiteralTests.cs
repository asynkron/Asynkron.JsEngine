using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for regex LITERALS (<c>/pat/flags</c>) inside the resumable VM
///     (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />). A generator/async whose body
///     evaluates a regex literal is now admitted by
///     <see cref="UnifiedBytecodeProductionEligibility.EvaluateResumable" />; before this slice the
///     resumable opcode allowlist (<c>TryFindUnsupportedResumableOpcode</c>) omitted
///     <see cref="UnifiedBytecodeOpCode.LoadRegexLiteral" />, so any regex literal in a generator/async
///     body fell back to the interpreter. This closes burn-down item B22.
///
///     <see cref="UnifiedBytecodeOpCode.LoadRegexLiteral" /> is a pure constant materialization: it reads
///     the interned pattern string and the encoded flags byte from the program and builds a fresh
///     <c>RegExp</c> object via <c>RegExpHelper.CreateRegExpLiteral</c> against the realm. It carries no
///     <c>AwaitedProgram</c> and touches nothing on the operand stack across a suspension — it simply
///     pushes one freshly created object — so it always runs to completion inside a single resumable step
///     and needs no resume-state restoration. The resumable handler is the literal twin of the sync VM's
///     handler, so per-evaluation fresh-object semantics (each evaluation yields a distinct <c>RegExp</c>
///     with its own <c>lastIndex</c>) are preserved.
///
///     Each proof asserts (a) ROUTING — eligibility via <c>EvaluateResumable</c> plus, for the end-to-end
///     runs, the resumable fast-path log (a fall-back to the interpreter fails the test) — and (b)
///     correctness for the adversarial cases: a regex matching across a yield, the per-evaluation
///     fresh-object identity guarantee in a loop, flag round-tripping, and an async body evaluating a
///     regex after an await suspension.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableRegexLiteralTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // The gate: a generator that evaluates a regex literal is now admitted, and the admitted program
    // actually carries LoadRegexLiteral (proving the slice expanded eligibility to the regex-literal tier
    // rather than admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_RegexLiteral_AdmitsLoadRegexLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 1;
                yield /ab+c/gi;
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

    // The gate: an async function evaluating a regex literal is admitted and carries LoadRegexLiteral.
    [Fact]
    public void EvaluateResumable_AsyncRegexLiteral_AdmitsLoadRegexLiteral()
    {
        var plan = GetFunctionPlan("""
            async function run(p) {
                await p;
                return /x/;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadRegexLiteral);
    }

    // End-to-end: a generator yields a regex literal whose source and flags round-trip exactly, and the
    // regex is functional (matches as expected) after the resume.
    [Fact(Timeout = 5000)]
    public async Task GeneratorRegexLiteral_RoutesResumableAndMatches()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield 1;
                yield /ab+c/gi;
            }

            var it = g();
            var a = it.next().value;       // 1
            var re = it.next().value;      // /ab+c/gi
            a + "|" + re.source + "|" + re.flags + "|" + re.test("xxABBBC");
            """);

        Assert.Equal("1|ab+c|gi|true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial: a regex literal evaluated on each loop turn must produce a FRESH object every time
    // (ECMAScript requires a new RegExp per evaluation). Two regexes captured across separate yields must
    // be distinct objects, each with its own lastIndex, even though the source text is identical.
    [Fact(Timeout = 5000)]
    public async Task GeneratorRegexLiteral_ProducesFreshObjectPerEvaluation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                while (true) {
                    yield /a/g;
                }
            }

            var it = g();
            var r1 = it.next().value;
            var r2 = it.next().value;
            r1.lastIndex = 5;             // mutate one; the other must be unaffected
            (r1 === r2) + "|" + r1.lastIndex + "|" + r2.lastIndex;
            """);

        Assert.Equal("false|5|0", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // End-to-end async: an async function evaluates a regex literal AFTER an await suspension point and
    // returns the match result. Proves LoadRegexLiteral runs correctly on a resumed step.
    [Fact(Timeout = 5000)]
    public async Task AsyncRegexLiteralAcrossAwait_RoutesResumableAndMatches()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(p) {
                await p;
                var re = /\d{3}/;          // regex literal evaluated on the resumed step
                return re.test("abc123") + "|" + re.source;
            }

            run(Promise.resolve(0)).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal("true|\\d{3}", result);
        AssertAsyncFastPath("run", argc: 1);
    }

    // Adversarial: a regex literal carrying escape sequences and the full flag set must round-trip its
    // source verbatim and stay functional after a yield. Exercises the pattern-string interning and the
    // flags-byte decode in the resumable LoadRegexLiteral handler against a non-trivial pattern.
    [Fact(Timeout = 5000)]
    public async Task GeneratorRegexLiteralWithEscapesAndAllFlags_RoundTrips()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield 1;
                yield /\b\w+\s*\d+/gimsuy;
            }

            var it = g();
            var a = it.next().value;       // 1
            var re = it.next().value;
            a + "|" + re.source + "|" + re.flags + "|" + re.test("word  42") + "|" + re.global;
            """);

        Assert.Equal("1|\\b\\w+\\s*\\d+|gimsuy|true|true", result);
        AssertGeneratorFastPath("g", argc: 0);
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
