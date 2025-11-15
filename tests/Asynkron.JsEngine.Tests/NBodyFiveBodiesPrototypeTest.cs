using Asynkron.JsEngine;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class NBodyFiveBodiesPrototypeTest
{
    [Fact]
    public async Task FiveBodies_PrototypeMethod_Works()
    {
        await using var engine = new JsEngine();

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
