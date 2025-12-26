using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class NBodyFiveBodiesPrototypeTestBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact]
    public async Task FiveBodies_PrototypeMethod_Works()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate(@"
            function Body(x) {
                this.x = x;
            }

            function NBodySystem(bodies) {
                this.bodies = bodies;
            }

            NBodySystem.prototype.getCount = function() {
                return this.bodies.length;
            };

            function make(i) {
                return new Body(i);
            }

            var sys = new NBodySystem( Array(make(1), make(2), make(3), make(4), make(5)) );
            sys.getCount();
        ");

        Assert.Equal(5.0, result);
    }
}

public class FastPath_NBodyFiveBodiesPrototypeTest(ITestOutputHelper output) : NBodyFiveBodiesPrototypeTestBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class Reference_NBodyFiveBodiesPrototypeTest(ITestOutputHelper output) : NBodyFiveBodiesPrototypeTestBase(output)
{
    protected override bool EnableFastPaths => false;
}
