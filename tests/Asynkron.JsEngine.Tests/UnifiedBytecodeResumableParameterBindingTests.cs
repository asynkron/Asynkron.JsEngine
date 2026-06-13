using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for eager IteratorBindingInitialization on the resumable route: generators
///     and async generators with non-simple parameter lists (destructuring patterns, defaults,
///     rest) bind their parameters in a transient environment at invocation time and seed the
///     flat parameter slots, instead of failing explicitly. Parameter expressions whose
///     function literals capture the function's own parameters stay declined (the transient
///     binding environment is discarded, so a retained closure over it would desynchronize
///     from later slot mutations).
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableParameterBindingTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";

    [Fact(Timeout = 5000)]
    public async Task GeneratorArrayPatternParameter_BindsAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g([a, b]) {
                yield a + b;
            }

            g([1, 2]).next().value;
            """);

        Assert.Equal(3d, result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorDefaultParameter_BindsAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(a = 41) {
                yield a + 1;
            }

            g().next().value;
            """);

        Assert.Equal(42d, result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorDefaultUsesEarlierParameter_BindsInOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(a, b = a * 2) {
                yield a + b;
            }

            g(14).next().value;
            """);

        Assert.Equal(42d, result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorRestParameter_BindsAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(first, ...xs) {
                yield first + xs.length;
            }

            g(40, 1, 2).next().value;
            """);

        Assert.Equal(42d, result);
        AssertGeneratorFastPath("g", argc: 3);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorObjectPatternWithRenameAndNestedDefault_BindsAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g({x, y: renamed = 2}) {
                yield x + renamed;
            }

            g({x: 40}).next().value;
            """);

        Assert.Equal(42d, result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorPatternBindingError_ThrowsAtCallNotFirstResume()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g([a]) {
                yield a;
            }

            var timing;
            try {
                var iterator = g(null);
                timing = "no-throw-at-call";
            } catch (error) {
                timing = error instanceof TypeError ? "throws-at-call" : "other";
            }
            timing;
            """);

        Assert.Equal("throws-at-call", result);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorBodyMutatesDestructuredParameter_AcrossYields()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g([a]) {
                a = a + 1;
                yield a;
                a = a * 2;
                yield a;
            }

            var iterator = g([20]);
            iterator.next().value + ":" + iterator.next().value;
            """);

        Assert.Equal("21:42", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorParameterDefaultWithNonCapturingFunctionLiteral_BindsAndInfersName()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g([arrow = () => 42]) {
                var value = arrow();
                yield arrow.name + ":" + value;
            }

            g([]).next().value;
            """);

        Assert.Equal("arrow:42", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorArrayPatternParameter_BindsAndSettles()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output;
            async function* g([a, b]) {
                yield a + b;
            }

            g([20, 22]).next().then(r => output = r.value);
            output;
            """);

        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorParameterDefaultCapturingOwnParameter_RoutesThroughIrFallback()
    {
        // A parameter default that closes over one of the function's own parameters is declined
        // by the resumable unified route (the transient binding environment is discarded after
        // seeding flat slots, so a retained closure would desynchronize). Rather than hard-throw,
        // such bodies now route to the IR generator fallback, which performs full
        // FunctionDeclarationInstantiation and binds the closure against a live parameter
        // environment, so the default-captured parameter is observed correctly.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(a, b = () => a) {
                yield b();
            }

            g(1).next().value;
            """);

        Assert.Equal(1d, result);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));
}
