using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for free / dynamic IDENTIFIER resolution inside the resumable VM
///     (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />). A generator/async whose body
///     READS a free variable (<c>yield outerVar</c>) or CALLS a free function (<c>yield helper(x)</c>
///     where <c>helper</c> is module/script-level or a captured outer binding) is now admitted by
///     <see cref="UnifiedBytecodeProductionEligibility.EvaluateResumable" />. Resolution lowers to
///     <see cref="UnifiedBytecodeOpCode.LoadDynamicIdentifier" /> /
///     <see cref="UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget" /> and runs against the live
///     closure environment threaded onto <see cref="UnifiedBytecodeResumeState.CallingEnvironment" />
///     (#3108), which is captured at construction and stable across yield/await suspension.
///
///     Each proof asserts (a) ROUTING — eligibility via EvaluateResumable plus the resumable fast-path
///     log for the end-to-end run (a fall-back to the interpreter fails the test) — and (b) correctness
///     for the adversarial cases the skeptic attacks: closure capture mutated across yields, outer code
///     mutating a captured/global binding while the frame is suspended, a free call rebound between
///     yields, and a TDZ free binding still throwing ReferenceError.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableDynamicIdentifierTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // The gate: a generator that READS a free variable and CALLS a free function between yields is now
    // admitted, and the admitted program actually carries the dynamic-identifier opcodes (proving the
    // slice expanded eligibility to the free/dynamic tier rather than admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_FreeReadAndFreeCallBetweenYields_AdmitsDynamicOpcodes()
    {
        var plan = GetFunctionPlan("""
            var base = 5;
            function helper(x) { return x + 1; }
            function* g(p) {
                yield base;
                yield helper(p);
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifier);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorDirectEvalThenOrdinaryDynamicReadStoreCall_RoutesResumableAndProducesValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var externalValue = 20;
            function helper(value) {
                return value + 1;
            }

            function* run(delta) {
                eval("'non-injecting direct eval'");
                externalValue = externalValue + delta;
                yield helper(externalValue);
            }

            var it = run(21);
            it.next().value + ":" + externalValue;
            """);

        Assert.Equal("42:41", result);
        AssertGeneratorFastPath("run", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorDirectEvalDeclarationDynamicLookup_StaysOnIrPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* run() {
                eval("var value = 41;");
                yield value + 1;
            }

            run().next().value;
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-resumable-generator-fast-path func=run argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClosureWithLiveWithObjectAfterDirectEval_StillDeclines()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function mk(scope) {
                var read;
                with (scope) {
                    read = function* () {
                        eval("'non-injecting direct eval'");
                        yield value;
                    };

                    return read;
                }
            }

            var fn = mk({ value: 42 });
            fn().next().value;
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-resumable-generator-fast-path func=read",
                StringComparison.Ordinal));
    }

    // End-to-end generator: a free READ and a free CALL between two yields route through the resumable
    // fast path and produce the correct value sequence.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreeReadAndCallBetweenYields_RoutesResumableAndProducesValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var base = 5;
            function helper(x) { return x * 10; }
            function* g(p) {
                yield base;
                yield helper(p);
            }

            var it = g(3);
            var first = it.next().value;
            var second = it.next().value;
            first + "|" + second;
            """);

        // base = 5, helper(3) = 30.
        Assert.Equal("5|30", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Skeptic concern #1: live capture across suspension. The generator READS a free binding `n` that a
    // SIBLING closure (`bump`) mutates; outer code calls `bump` between the generator's yields. Because
    // the generator resolves `n` by name against the live environment (not a value snapshot taken at
    // construction), each resumed step observes the freshly-mutated value. The generator performs no
    // free write itself here. (`n` is module/script-level so it resolves dynamically by name; a binding
    // captured from an *enclosing function* scope resolves against the threaded CallingEnvironment too — the
    // captured READ and captured UPDATE (`n++`) tiers are both admitted now, see
    // UnifiedBytecodeResumableCapturedClosureWriteTests; the captured plain/compound STORE stays declined.)
    [Fact(Timeout = 5000)]
    public async Task GeneratorReadsFreeBindingMutatedByClosureSibling_ReadsLiveValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var n = 0;
            function bump() { n++; }
            function* gen() {
                yield n;
                yield n;
            }

            var it = gen();
            var a = it.next().value;   // n = 0
            bump();                    // mutate the captured binding while suspended
            bump();
            var b = it.next().value;   // n = 2 (live read)
            a + "|" + b;
            """);

        // The generator reads the live `n`: 0, then 2 after two bumps.
        Assert.Equal("0|2", result);
        AssertGeneratorFastPath("gen", argc: 0);
    }

    // Skeptic concern #2: outer code mutates a captured/global binding WHILE the generator is suspended.
    // The resumed step must see the new value, proving resolution is not snapshotted at construction.
    [Fact(Timeout = 5000)]
    public async Task GeneratorGlobalMutatedBetweenYields_ResumedStepSeesNewValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var shared = 1;
            function* g() {
                yield shared;
                yield shared;
            }

            var it = g();
            var first = it.next().value;
            shared = 99;            // mutate while the generator is suspended at the first yield
            var second = it.next().value;
            first + "|" + second;
            """);

        // first reads 1; after outer mutation the resumed step reads 99.
        Assert.Equal("1|99", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Skeptic concern #3: a free function call must resolve to the CURRENT binding after a resume. The
    // global `pick` is reassigned while the generator is suspended; the resumed step must call the new
    // function, not the one bound when the generator object was created.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreeCallReboundBetweenYields_ResumedStepCallsNewBinding()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var pick = function () { return "first"; };
            function* g() {
                yield pick();
                yield pick();
            }

            var it = g();
            var a = it.next().value;
            pick = function () { return "second"; };   // rebind while suspended
            var b = it.next().value;
            a + "|" + b;
            """);

        Assert.Equal("first|second", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Skeptic concern #4: a TDZ / not-yet-initialized free binding referenced in the generator body must
    // still throw ReferenceError when the step that reads it runs.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreeReadOfUninitializedBinding_ThrowsReferenceError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield 1;
                yield later;   // `later` is in TDZ until the let below initializes it
            }

            var it = g();
            var first = it.next().value;
            var caught = "none";
            try {
                it.next();
            } catch (e) {
                caught = e.constructor.name;
            }
            first + "|" + caught;
            """);

        // The lexical `later` is never initialized in the reachable order before the second step reads
        // it, so the resumed step throws a ReferenceError.
        Assert.Equal("1|ReferenceError", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // End-to-end async: an async function that READS a free variable (`await factor`) and CALLS a free
    // function (`return scale(o.value)`) across await suspension points routes through the resumable
    // fast path and resolves with the correct value. The free call dispatches from a resumed frame.
    [Fact(Timeout = 5000)]
    public async Task AsyncFreeReadAndCallAcrossAwaits_RoutesResumableAndResolves()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            var factor = 2;
            function scale(n) { return n * factor; }
            async function run(o) {
                await factor;            // free READ across the first await boundary
                await o.second;
                return scale(o.value);   // free CALL in the trailing return, after suspension
            }

            run({ second: Promise.resolve(0), value: 10 })
                .then(value => asyncResult = value);
            asyncResult;
            """);

        // `await factor` reads the free `factor` (2); the trailing `return scale(o.value)` is a free
        // call resolved after suspension: scale(10) = 10 * factor = 20.
        Assert.Equal(20d, result);
        AssertAsyncFastPath("run", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncDirectEvalThenOrdinaryDynamicReadStoreCall_RoutesResumableAndResolves()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            var externalValue = 20;
            function helper(value) {
                return value + 1;
            }

            async function run(delta) {
                eval("'non-injecting direct eval'");
                externalValue = externalValue + delta;
                await 0;
                return helper(externalValue);
            }

            run(21).then(value => asyncResult = value + ":" + externalValue);
            asyncResult;
            """);

        Assert.Equal("42:41", result);
        AssertAsyncFastPath("run", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncDirectEvalArgumentsRead_StaysOnIrPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(value) {
                return eval("arguments[0]");
            }

            run(42).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-resumable-async-fast-path func=run argc=1",
                StringComparison.Ordinal));
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
