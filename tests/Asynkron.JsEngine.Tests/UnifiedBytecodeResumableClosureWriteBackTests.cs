using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for closure write-back in routed resumable bodies: closures created against
///     the materialized body environment (hoisted helpers, class members, constructors) must
///     have their WRITES to captured generator bindings visible to subsequent flat-slot reads.
///     Pins two fixes: write-only captures count as activation captures (slot-target mutation
///     instructions are instruction metadata, not expression-program ops), and call/construct
///     boundaries re-sync the materialized environment into the flat slots after returning.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableClosureWriteBackTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";

    [Fact(Timeout = 5000)]
    public async Task HelperWriteOnlyParamCapture_WriteIsVisibleToGenerator()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(p) {
                yield 1;
                function h() {
                    p = 9;
                }
                h();
                yield p;
            }

            var iterator = g(7);
            iterator.next();
            iterator.next().value;
            """);

        Assert.Equal(9d, result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task HelperCompoundWriteCapture_WriteIsVisibleToGenerator()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(p) {
                yield 1;
                function h() {
                    p = p + 2;
                }
                h();
                h();
                yield p;
            }

            var iterator = g(7);
            iterator.next();
            iterator.next().value;
            """);

        Assert.Equal(11d, result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task HelperWriteLocalCapture_WriteIsVisibleToGenerator()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield 1;
                var v = 7;
                function h() {
                    v = 9;
                }
                h();
                yield v;
            }

            var iterator = g();
            iterator.next();
            iterator.next().value;
            """);

        Assert.Equal(9d, result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task MemberCallCapture_WriteIsVisibleToGenerator()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(count) {
                yield "ready";
                class Box {
                    bump() {
                        count = count + 1;
                    }
                }
                var box = new Box();
                box.bump();
                box.bump();
                yield count;
            }

            var iterator = g(0);
            iterator.next();
            iterator.next().value;
            """);

        Assert.Equal(2d, result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task ConstructorCapture_WriteIsVisibleToGenerator()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(count) {
                yield "ready";
                class Box {
                    constructor() {
                        count = count + 1;
                        this.n = count;
                    }
                }
                new Box();
                new Box();
                yield count;
            }

            var iterator = g(0);
            iterator.next();
            iterator.next().value;
            """);

        Assert.Equal(2d, result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task ConstructorCapture_ReadsLiveBindingAtConstructTime()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                class Box {
                    constructor() {
                        this.value = seed;
                    }
                }
                var early = new Box();
                seed = seed + 1;
                var late = new Box();
                yield early.value + ":" + late.value;
            }

            var iterator = g(6);
            iterator.next();
            iterator.next().value;
            """);

        Assert.Equal("6:7", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task ConstructorCapture_SurvivesGeneratorCompletion()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var Made;
            function* g(seed) {
                yield "ready";
                class Box {
                    constructor() {
                        this.value = seed * 2;
                    }
                }
                Made = Box;
                yield "done";
            }

            var iterator = g(21);
            iterator.next();
            iterator.next();
            iterator.next();
            new Made().value;
            """);

        Assert.Equal(42d, result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task DerivedConstructorCapture_SuperAndCapturedStateBothApply()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor(value) {
                    this.base = value;
                }
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    constructor() {
                        super(seed);
                        this.extra = seed + 1;
                    }
                }
                var box = new Box();
                yield box.base + ":" + box.extra + ":" + (box instanceof Base);
            }

            var iterator = g(41);
            iterator.next();
            iterator.next().value;
            """);

        Assert.Equal("41:42:true", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));
}
