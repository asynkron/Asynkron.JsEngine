using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class NBodyExactReproductionTest(ITestOutputHelper output)
{
    [Fact]
    public async Task NBodySystem_ConstructorLogic_Works()
    {
        await using var engine = new JsEngine();

        // Add debug output
        engine.SetGlobalFunction("__log", args =>
        {
            output.WriteLine(string.Join(" ", args.Select(a => a?.ToString() ?? "null")));
            return null;
        });

        var result = await engine.Evaluate(@"
            var SOLAR_MASS = 4 * 3.14 * 3.14;

            function Body(vx) {
                __log('Body constructor, vx:', vx);
                this.vx = vx;
            }

            Body.prototype.offsetMomentum = function(px) {
                __log('offsetMomentum called with px:', px);
                this.vx = -px / SOLAR_MASS;
                return this;
            };

            function Sun() {
                __log('Sun called');
                var result = new Body(0.0);
                __log('Sun returning:', result);
                return result;
            }

            function Jupiter() {
                __log('Jupiter called');
                return new Body(1.0);
            }

            function NBodySystem(bodies) {
                __log('NBodySystem constructor, bodies:', bodies, 'length:', bodies.length);
                this.bodies = bodies;
                var size = this.bodies.length;
                __log('size:', size);
                __log('bodies[0]:', this.bodies[0]);
                __log('bodies[0].vx:', this.bodies[0].vx);
                __log('About to call offsetMomentum on bodies[0]');
                this.bodies[0].offsetMomentum(10.0);
                __log('offsetMomentum called successfully');
            }

            __log('Creating bodies array with Array(Sun(), Jupiter())');
            var bodies = new NBodySystem( Array(Sun(), Jupiter()) );
            __log('NBodySystem created');
            'done';
        ");

        Assert.Equal("done", result);
    }
}
