using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class NBodyArraySizeTestBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task ArraySize_PrototypeMethod_Works(int count)
    {
        await using var engine = CreateEngine();

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

public class FastPathNBodyArraySizeTest(ITestOutputHelper output) : NBodyArraySizeTestBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class ReferenceNBodyArraySizeTest(ITestOutputHelper output) : NBodyArraySizeTestBase(output)
{
    protected override bool EnableFastPaths => false;
}
