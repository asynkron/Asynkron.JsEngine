using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for the <c>import.meta</c> meta-property (<see cref="UnifiedBytecodeOpCode.LoadImportMeta" />)
///     inside the resumable VM (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />) — burn-down item
///     B20. Before this slice the resumable opcode allow-list (<c>TryFindUnsupportedResumableOpcode</c>) omitted
///     <see cref="UnifiedBytecodeOpCode.LoadImportMeta" />, so any generator/async body that read
///     <c>import.meta</c> fell back to the interpreter even though the synchronous VM already admitted it.
///
///     Semantics: <c>import.meta</c> is a pure meta-property read that resolves a single
///     <c>Symbol.ImportMeta</c> binding. The resumable handler resolves it against the live closure environment
///     threaded onto <see cref="UnifiedBytecodeResumeState.CallingEnvironment" /> — the captured MODULE
///     environment, stable across <c>yield</c>/<c>await</c> — so the SAME per-module <c>import.meta</c> object is
///     observed on every step including across a suspension. The opcode pushes one value, carries no
///     <c>AwaitedProgram</c>, and cannot itself suspend.
///
///     <c>import.meta</c> is ONLY bound inside a module environment; outside a module the binding is absent and
///     the resumable handler surfaces the same <c>ReferenceError</c> as the sync VM via the resumable Throw step
///     (the resumable loop carries no <c>ThrowSignal</c> catch, so the handler sets the throw directly rather than
///     raising one). These proofs therefore run in MODULE context (<c>EvaluateModule</c>).
///
///     Each proof asserts (a) ROUTING — eligibility via <c>EvaluateResumable</c> plus, for the end-to-end runs,
///     the resumable fast-path log (an interpreter fall-back fails the test) — and (b) correctness, including the
///     adversarial case of <c>import.meta</c> read AFTER an await suspension resolving to the SAME stable object.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
[Category(TestCategories.ModuleSystem)]
public sealed class UnifiedBytecodeResumableImportMetaTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";

    // The gate: an async function that reads `import.meta` after an await is admitted and carries
    // LoadImportMeta (proving the slice expanded eligibility to the import.meta opcode).
    [Fact]
    public void EvaluateResumable_AsyncImportMetaAfterAwait_AdmitsLoadImportMeta()
    {
        var plan = GetFunctionPlan("""
            async function run(p) {
                await p;
                return import.meta;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadImportMeta);
    }

    // The gate (generator): a generator that yields `import.meta` across a suspension is admitted and carries
    // LoadImportMeta.
    [Fact]
    public void EvaluateResumable_GeneratorImportMetaAcrossYield_AdmitsLoadImportMeta()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 1;
                yield import.meta;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadImportMeta);
    }

    // End-to-end async (module): an async function reads `typeof import.meta` after an await; the resumable
    // handler resolves the module's import.meta binding against the threaded CallingEnvironment.
    [Fact(Timeout = 5000)]
    public async Task AsyncImportMetaAfterAwaitInModule_RoutesResumableAndIsObject()
    {
        await using var engine = CreateEngine();
        await engine.EvaluateModule("""
            globalThis.metaType = "PENDING";
            async function run(p) { await p; return typeof import.meta; }
            run(Promise.resolve(0)).then(value => globalThis.metaType = value);
            "started";
            """, "import-meta-async.js");

        Assert.Equal("object", await engine.Evaluate("globalThis.metaType"));
        AssertAsyncFastPath("run", argc: 1);
    }

    // Adversarial (module): the SAME import.meta object identity must hold across the await suspension — the
    // object captured before the await is `===` the object read after it.
    [Fact(Timeout = 5000)]
    public async Task AsyncImportMetaIdentityAcrossAwaitInModule_IsStableObject()
    {
        await using var engine = CreateEngine();
        await engine.EvaluateModule("""
            globalThis.metaStable = "PENDING";
            // Capture import.meta BEFORE the await into a local slot, then compare the post-await read against
            // it: same per-module object identity must survive the suspension. The local `before` is a flat
            // activation slot (not a captured outer binding), so the body stays on the resumable route.
            async function run(p) { var before = import.meta; await p; return import.meta === before; }
            run(Promise.resolve(0)).then(value => globalThis.metaStable = value);
            "started";
            """, "import-meta-identity.js");

        Assert.Equal(true, await engine.Evaluate("globalThis.metaStable"));
        AssertAsyncFastPath("run", argc: 1);
    }

    // End-to-end generator (module): a generator yields import.meta across a suspension; the resumed step
    // resolves the SAME stable module object.
    [Fact(Timeout = 5000)]
    public async Task GeneratorImportMetaAcrossYieldInModule_RoutesResumableAndIsStable()
    {
        await using var engine = CreateEngine();
        await engine.EvaluateModule("""
            const expected = import.meta;
            function* g() { yield 1; yield (import.meta === expected); }
            const it = g();
            globalThis.metaGen = it.next().value + "|" + it.next().value;
            "started";
            """, "import-meta-gen.js");

        Assert.Equal("1|true", await engine.Evaluate("globalThis.metaGen"));
        AssertGeneratorFastPath("g", argc: 0);
    }

    private void AssertAsyncFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
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
