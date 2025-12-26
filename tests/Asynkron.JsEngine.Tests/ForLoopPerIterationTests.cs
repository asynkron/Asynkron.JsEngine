using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Tests.Helpers;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Layered reproductions of test262 for-loop per-iteration scope semantics.
/// These mirror key failing test262 cases so we can debug without the full harness.
/// </summary>
public abstract class ForLoopPerIterationTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
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
            nameof(ForLoopPerIterationTestsBase),
            new XunitLogger(Output, nameof(ForLoopPerIterationTestsBase)),
            EnableFastPaths);
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
            nameof(ForLoopPerIterationTestsBase),
            new XunitLogger(Output, nameof(ForLoopPerIterationTestsBase)),
            EnableFastPaths);
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
            nameof(ForLoopPerIterationTestsBase),
            new XunitLogger(Output, nameof(ForLoopPerIterationTestsBase)),
            EnableFastPaths);
        var result = await engine.Evaluate(source);
        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(0d, array.GetElement(0).ToObject());
        Assert.Equal(1d, array.GetElement(1).ToObject());
        Assert.Equal(2d, array.GetElement(2).ToObject());
    }
}

public class FastPath_ForLoopPerIterationTests(ITestOutputHelper output) : ForLoopPerIterationTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class Reference_ForLoopPerIterationTests(ITestOutputHelper output) : ForLoopPerIterationTestsBase(output)
{
    protected override bool EnableFastPaths => false;
}
