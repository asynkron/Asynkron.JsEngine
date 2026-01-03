using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Tests.Helpers;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class NestedSlotStampingTests
{
    [Fact]
    public async Task NestedClosure_UsesSlotFastPath()
    {
        var logger = new TestLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            function make() {
                let x = 0;
                return function () { return x++; };
            }
            const f = make();
            [f(), f(), f()];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(0.0, array.GetElement(0).AsDouble());
        Assert.Equal(1.0, array.GetElement(1).AsDouble());
        Assert.Equal(2.0, array.GetElement(2).AsDouble());

        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
        Assert.Contains(messages, m =>
            m.Contains("Identifier slot read hit", StringComparison.OrdinalIgnoreCase) &&
            m.Contains("name=x", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MultipleNestedClosures_DoNotCollideAcrossInstances()
    {
        var logger = new TestLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            function outer(start) {
                let x = start;
                function inner() { return x++; }
                return inner;
            }
            const f = outer(1);
            const g = outer(10);
            [f(), f(), g(), g()];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(1.0, array.GetElement(0).AsDouble());
        Assert.Equal(2.0, array.GetElement(1).AsDouble());
        Assert.Equal(10.0, array.GetElement(2).AsDouble());
        Assert.Equal(11.0, array.GetElement(3).AsDouble());

        var scopeIds = logger.Collector.Snapshot()
            .Select(r => ExtractScopeId(r.Message))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToArray();

        Assert.True(scopeIds.Distinct().Count() >= 2, "Distinct closures should use different scope ids");
    }

    private static int? ExtractScopeId(string message)
    {
        if (!message.Contains("Identifier slot", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = Regex.Match(message, @"scopeId=(?<id>-?\d+)");
        return match.Success && int.TryParse(match.Groups["id"].Value, out var value) ? value : null;
    }
}
