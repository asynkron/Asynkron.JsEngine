using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Layered reproductions of test262 for-loop per-iteration scope semantics.
/// These mirror key failing test262 cases so we can debug without the full harness.
/// </summary>
[Category(TestCategories.ScopeAnalysis)]
public sealed class ForLoopPerIterationTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task ForLoop_LexicalBindings_AreFreshPerIteration_ScopeBodyLexOpen()
    {
        // Mirrors test262: language/statements/for/scope-body-lex-open.js
        const string source = """
            let probeBefore, probeTest, probeIncr, probeBody;
            let run = true;

            for (
                let x = 'outside', _ = probeBefore = () => x;
                run && (x = 'inside', probeTest = () => x);
                probeIncr = () => x
            ) {
                probeBody = () => x;
                run = false;
            }

            [
                probeBefore(),
                probeTest(),
                probeBody(),
                probeIncr()
            ];
            """;

        await using var engine = TestEngineFactory.CreateDebugEngine(
            new TestLogger(Output, nameof(ForLoopPerIterationTests)));
        var result = await engine.Evaluate(source);
        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("outside", array.GetElement(0).ToObject());
        Assert.Equal("inside", array.GetElement(1).ToObject());
        Assert.Equal("inside", array.GetElement(2).ToObject());
        Assert.Equal("inside", array.GetElement(3).ToObject());
    }

    [Fact]
    public async Task ForLoop_LexicalBindings_AreFreshPerIteration_MultiLet()
    {
        // Mirrors test262: language/statements/let/syntax/let-iteration-variable-is-freshly-allocated-for-each-iteration-multi-let-binding.js
        const string source = """
            var probes = [];
            for (let a = 0, b = 1; a < 3; ++a, ++b) {
              probes.push([() => a, () => b]);
            }
            // Collect snapshots
            [
              probes[0][0](), probes[0][1](),
              probes[1][0](), probes[1][1](),
              probes[2][0](), probes[2][1](),
            ];
            """;

        await using var engine = TestEngineFactory.CreateDebugEngine(
            new TestLogger(Output, nameof(ForLoopPerIterationTests)));
        var result = await engine.Evaluate(source);
        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(0d, array.GetElement(0).ToObject());
        Assert.Equal(1d, array.GetElement(1).ToObject());
        Assert.Equal(1d, array.GetElement(2).ToObject());
        Assert.Equal(2d, array.GetElement(3).ToObject());
        Assert.Equal(2d, array.GetElement(4).ToObject());
        Assert.Equal(3d, array.GetElement(5).ToObject());
    }

    [Fact]
    public async Task ForLoop_LexicalBindings_AreFreshPerIteration_SingleLet()
    {
        // Mirrors test262: language/statements/let/syntax/let-iteration-variable-is-freshly-allocated-for-each-iteration-single-let-binding.js
        const string source = """
            var probes = [];
            for (let a = 0; a < 3; ++a) {
              probes.push(() => a);
            }
            [probes[0](), probes[1](), probes[2]()];
            """;

        await using var engine = TestEngineFactory.CreateDebugEngine(
            new TestLogger(Output, nameof(ForLoopPerIterationTests)));
        var result = await engine.Evaluate(source);
        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(0d, array.GetElement(0).ToObject());
        Assert.Equal(1d, array.GetElement(1).ToObject());
        Assert.Equal(2d, array.GetElement(2).ToObject());
    }

    [Fact]
    public async Task ContinueInsideShadowingBlock_PreservesLoopBinding()
    {
        // Mirrors test262: language/statements/continue/shadowing-loop-variable-in-same-scope-as-continue.js
        const string source = """
            let count = 0;
            for (let x = 0; x < 10;) {
              x++;
              count++;
              {
                let x = 'hello';
                continue;
              }
            }
            count;
            """;

        await using var engine = TestEngineFactory.CreateDebugEngine(
            new TestLogger(Output, nameof(ForLoopPerIterationTests)));
        var result = await engine.Evaluate(source);

        Assert.Equal(10d, result);
    }
}
