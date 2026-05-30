using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for issue #2675: <c>this</c>-dependent async and generator functions are accepted by the
///     resumable unified bytecode production route. Mirrors the sync-route <c>this</c> support landed in
///     #2633/#2643. Covers strict vs sloppy coercion fidelity, async/generator <c>this</c>, and <c>this</c>
///     read after a suspension point (post-<c>await</c>/post-<c>yield</c>), plus negative-fallback gates that
///     must still decline before VM execution (new.target, arguments-object).
///
///     The proofs route <c>this</c> through resumable-supported opcodes only (LoadThis, Yield, Binary,
///     Return). Property reads such as <c>this.x</c> and <c>typeof</c> remain outside the resumable opcode
///     set and decline independently of the <c>this</c>-binding gate widened here.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableThisBindingTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";

    // AC-3/AC-4: a strict generator returns its bound this through the resumable fast path.
    [Fact(Timeout = 5000)]
    public async Task StrictGeneratorReturnsThis_UsesResumableUnifiedBytecodeFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            "use strict";
            function* gen() {
                return this;
            }

            gen.call(41).next().value;
            """);

        Assert.Equal(41d, result);
        AssertGeneratorFastPath("gen", argc: 0);
    }

    // AC-3/AC-4: a strict async function returns its bound this through the resumable fast path
    // (this is read after the await suspension point).
    [Fact(Timeout = 5000)]
    public async Task StrictAsyncReturnsThisAfterAwait_UsesResumableUnifiedBytecodeFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(input) {
                "use strict";
                await input;
                return this;
            }

            run.call(41, 0).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(41d, result);
        AssertAsyncFastPath("run", argc: 1);
    }

    // AC-4: this read before and after a yield suspension is the same binding.
    [Fact(Timeout = 5000)]
    public async Task GeneratorThisSurvivesYieldSuspension_UsesResumableUnifiedBytecodeFastPath()
    {
        await using var engine = CreateEngine();
        // this is read in the return, which runs after the yield suspension/resume, proving the bound
        // this survives on the resume state across the suspension point.
        var result = await engine.Evaluate("""
            "use strict";
            function* gen(input) {
                var resumed = yield input;
                return this;
            }

            var iterator = gen.call(7, 0);
            var first = iterator.next();
            var second = iterator.next();
            [first.value, first.done, second.value, second.done];
            """);

        var steps = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal(0d, steps.Items[0].AsDouble());
        Assert.False(steps.Items[1].AsBoolean());
        Assert.Equal(7d, steps.Items[2].AsDouble());
        Assert.True(steps.Items[3].AsBoolean());
        AssertGeneratorFastPath("gen", argc: 1);
    }

    // AC-4: this read before and after an await suspension is the same binding.
    [Fact(Timeout = 5000)]
    public async Task AsyncThisSurvivesAwaitSuspension_UsesResumableUnifiedBytecodeFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(input) {
                "use strict";
                var before = this;
                await input;
                return before === this;
            }

            run.call(99, 0).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(true, result);
        AssertAsyncFastPath("run", argc: 1);
    }

    // AC-4 fidelity: strict mode keeps a primitive this uncoerced (this === primitive arg).
    [Fact(Timeout = 5000)]
    public async Task StrictGeneratorPrimitiveThis_NotBoxed()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            "use strict";
            function* gen(x) {
                return this === x;
            }

            gen.call(42, 42).next().value;
            """);

        Assert.Equal(true, result);
        AssertGeneratorFastPath("gen", argc: 1);
    }

    // AC-4 fidelity: sloppy mode boxes a primitive this (this !== primitive arg), matching the sync route's
    // CoerceThisValueForNonStrict behaviour.
    [Fact(Timeout = 5000)]
    public async Task SloppyGeneratorPrimitiveThis_Boxed()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* gen(x) {
                return this === x;
            }

            gen.call(42, 42).next().value;
            """);

        Assert.Equal(false, result);
        AssertGeneratorFastPath("gen", argc: 1);
    }

    // AC-5 negative-fallback: new.target keeps declining the resumable route (LoadNewTarget gate intact).
    [Fact(Timeout = 5000)]
    public async Task GeneratorUsingNewTarget_DeclinesResumableUnifiedBytecodeFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* gen() {
                return new.target;
            }

            gen().next().value;
            """);

        Assert.Equal(Symbol.Undefined, result);
        AssertNotGeneratorFastPath("gen");
    }

    // AC-5 negative-fallback: arguments-object dependency keeps declining the resumable route.
    [Fact(Timeout = 5000)]
    public async Task GeneratorUsingArguments_DeclinesResumableUnifiedBytecodeFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* gen(a, b) {
                return arguments.length;
            }

            gen(1, 2, 3).next().value;
            """);

        Assert.Equal(3d, result);
        AssertNotGeneratorFastPath("gen");
    }

    // AC-5 negative-fallback: async function using arguments keeps declining the resumable route.
    [Fact(Timeout = 5000)]
    public async Task AsyncUsingArguments_DeclinesResumableUnifiedBytecodeFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(a, b) {
                await a;
                return arguments.length;
            }

            run(1, 2, 3).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(3d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=run",
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

    private void AssertNotGeneratorFastPath(string functionName) =>
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName}",
                StringComparison.Ordinal));
}
