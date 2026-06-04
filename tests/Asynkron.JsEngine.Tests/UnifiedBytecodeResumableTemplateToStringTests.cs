using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for the template-substitution <see cref="UnifiedBytecodeOpCode.ToString" /> coercion inside
///     the resumable VM (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />) — burn-down item B37.
///     Before this slice the resumable opcode allow-list (<c>TryFindUnsupportedResumableOpcode</c>) omitted
///     <see cref="UnifiedBytecodeOpCode.ToString" />, so any generator/async body whose untagged template
///     literal carried a substitution (<c>`v${x}`</c>) fell back to the interpreter even though the
///     synchronous VM already admitted it.
///
///     Semantics: an untagged template literal lowers each <c>${ }</c> hole to a <c>String(value)</c> coercion
///     (the <see cref="UnifiedBytecodeOpCode.ToString" /> opcode) before concatenation. The opcode pops/replaces
///     the operand in place, carries no <c>AwaitedProgram</c>, and cannot itself suspend; the value to coerce
///     sits on <see cref="UnifiedBytecodeResumeState.OperandStack" /> and is restored across any suspension in a
///     sibling sub-expression (<c>`v${yield 1}`</c>), exactly like the admitted unaries. The resumable handler is
///     the literal twin of the sync VM's (<c>JsOps.ToJsString</c>), so a throwing coercion surfaces as the
///     resumable Throw step.
///
///     Each proof asserts (a) ROUTING — eligibility via <c>EvaluateResumable</c> plus, for the end-to-end runs,
///     the resumable fast-path log (an interpreter fall-back fails the test) — and (b) correctness, including the
///     adversarial case of a substitution evaluated ACROSS the suspension.
///
///     SCOPE / honesty note: TAGGED templates (B21, <c>tag`a${x}b`</c>) are NOT admitted here. The tagged-template
///     CALL shape declines at the shared expression-eligibility gate (the general call-candidate predicates do not
///     recognize the <see cref="UnifiedBytecodeOpCode.LoadTemplateObject" />-plus-substitutions argument shape) on
///     BOTH the sync and resumable routes, so <c>LoadTemplateObject</c> is never reached by an admitted resumable
///     program; admitting it is real expression-level call-candidate infrastructure, out of scope for this
///     allow-list/handler slice.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableTemplateToStringTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // The gate: a generator whose template literal carries a substitution is admitted, and the admitted
    // program actually carries the ToString opcode (proving the slice expanded eligibility to ToString rather
    // than admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_GeneratorTemplateSubstitution_AdmitsToString()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                let x = 5;
                yield `v${x}`;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ToString);
    }

    // The gate (async): an async function whose template literal carries a substitution after an await is
    // admitted and carries ToString.
    [Fact]
    public void EvaluateResumable_AsyncTemplateSubstitutionAfterAwait_AdmitsToString()
    {
        var plan = GetFunctionPlan("""
            async function run(p) {
                await p;
                let x = 9;
                return `n=${x}`;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ToString);
    }

    // End-to-end: a generator yields a template literal whose substitution was bound BEFORE the relevant yield.
    [Fact(Timeout = 5000)]
    public async Task GeneratorTemplateSubstitution_RoutesResumableAndCoercesCorrectly()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield 1;
                let x = 5;
                yield `v${x}`;
            }

            var it = g();
            var a = it.next().value;   // 1
            var b = it.next().value;   // "v5"
            a + "|" + b;
            """);

        Assert.Equal("1|v5", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial: the substitution value is produced BY a yield, so it lands on the operand stack and must be
    // restored across the suspension before the ToString coercion runs on resume.
    [Fact(Timeout = 5000)]
    public async Task GeneratorTemplateSubstitutionAcrossYield_RestoresOperandAndCoerces()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield `mid=${yield "first"}`;
            }

            var it = g();
            var a = it.next().value;        // "first"
            var b = it.next(7).value;       // resumes; substitution value = 7 -> "mid=7"
            a + "|" + b;
            """);

        Assert.Equal("first|mid=7", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Correctness: a non-string substitution (number, boolean, null) is String()-coerced exactly as the sync VM.
    [Fact(Timeout = 5000)]
    public async Task GeneratorTemplateNonStringSubstitution_CoercesLikeSyncVm()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield 1;
                let n = 42;
                let b = true;
                yield `n=${n} b=${b} z=${null}`;
            }

            var it = g();
            it.next();
            it.next().value;
            """);

        Assert.Equal("n=42 b=true z=null", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // End-to-end async: an async function returns a template literal after an await; the promise fulfils with
    // the coerced string.
    [Fact(Timeout = 5000)]
    public async Task AsyncTemplateSubstitutionAfterAwait_RoutesResumableAndCoerces()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = "PENDING";
            async function run(p) {
                await p;
                let x = 9;
                return `n=${x}`;
            }

            run(Promise.resolve(0)).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal("n=9", result);
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
