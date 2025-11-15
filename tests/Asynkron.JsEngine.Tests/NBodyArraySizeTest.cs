using Asynkron.JsEngine;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class NBodyArraySizeTest
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task ArraySize_PrototypeMethod_Works(int count)
    {
        await using var engine = new JsEngine();

        var makes = string.Join(", ", Enumerable.Range(1, count).Select(i => $"make({i})"));

        var script = $@"
            function Body(x) {{
                this.x = x;
            }}

            function NBodySystem(bodies) {{
                this.bodies = bodies;
            }}

            NBodySystem.prototype.getCount = function() {{
                return this.bodies.length;
            }};

            function make(i) {{
                return new Body(i);
            }}

            var sys = new NBodySystem( Array({makes}) );
            sys.getCount();
        ";

        var result = await engine.Evaluate(script);

        Assert.Equal((double)count, result);
    }
}
