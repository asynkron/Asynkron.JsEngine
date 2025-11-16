namespace Asynkron.JsEngine.Tests;

public class NBodyStepByStepTest
{
    [Fact]
    public async Task Step1_Array_With_Constructor_Calls()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function Body(x) {
                this.x = x;
            }

            function Sun() {
                return new Body(1);
            }

            function Jupiter() {
                return new Body(2);
            }

            var arr = Array(Sun(), Jupiter());
            arr.length;
        ");

        Assert.Equal(2.0, result);
    }

    [Fact]
    public async Task Step2_PassArrayToConstructor()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function NBodySystem(bodies) {
                this.bodies = bodies;
            }

            var arr = Array(1, 2);
            var sys = new NBodySystem(arr);
            sys.bodies.length;
        ");

        Assert.Equal(2.0, result);
    }

    [Fact]
    public async Task Step3_CombinedInOneExpression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function NBodySystem(bodies) {
                this.bodies = bodies;
            }

            var sys = new NBodySystem( Array(1, 2) );
            sys.bodies.length;
        ");

        Assert.Equal(2.0, result);
    }
}
