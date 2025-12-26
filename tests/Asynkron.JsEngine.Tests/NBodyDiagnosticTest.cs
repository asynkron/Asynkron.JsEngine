using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class NBodyDiagnosticTestBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact]
    public async Task Array_ConstructorWithMultipleArguments_Works()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Array(1, 2, 3);");

        Assert.IsType<JsArray>(result);
        var arr = (JsArray)result;
        Assert.Equal(3, arr.Length);
        Assert.Equal(1.0, arr.Get(0));
        Assert.Equal(2.0, arr.Get(1));
        Assert.Equal(3.0, arr.Get(2));
    }

    [Fact]
    public async Task Array_ConstructorWithFunctionResults_Works()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function getNum() { return 42; }
            Array(getNum(), getNum());
        ");

        Assert.IsType<JsArray>(result);
        var arr = (JsArray)result;
        Assert.Equal(2, arr.Length);
        Assert.Equal(42.0, arr.Get(0));
        Assert.Equal(42.0, arr.Get(1));
    }

    [Fact]
    public async Task Array_ConstructorWithObjectResults_Works()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function makeObj(x) {
                return { value: x };
            }
            var arr = Array(makeObj(1), makeObj(2));
            arr;
        ");

        Assert.IsType<JsArray>(result);
        var arr = (JsArray)result;
        Assert.Equal(2, arr.Length);
        Assert.IsType<JsObject>(arr.Get(0).ToObject());
        Assert.IsType<JsObject>(arr.Get(1).ToObject());
    }

    [Fact]
    public async Task NBody_SimplifiedScenario_Works()
    {
        await using var engine = CreateEngine();
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

            function NBodySystem(bodies) {
                this.bodies = bodies;
            }

            // This is what fails in nbody test
            var bodies = new NBodySystem( Array(Sun(), Jupiter()) );
            bodies.bodies.length;
        ");

        Assert.Equal(2.0, result);
    }
}

public class FastPath_NBodyDiagnosticTest(ITestOutputHelper output) : NBodyDiagnosticTestBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class Reference_NBodyDiagnosticTest(ITestOutputHelper output) : NBodyDiagnosticTestBase(output)
{
    protected override bool EnableFastPaths => false;
}
