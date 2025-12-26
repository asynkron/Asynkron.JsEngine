using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class FunctionConstructorTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact]
    public async Task NewFunctionCreatesCallableBody()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("(new Function('a', 'b', 'return a + b;'))(2, 3);");

        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task NewFunctionCanBuildTypedArraySubclass()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            (function() {
              const Ctor = new Function('return class MyUint8Array extends Uint8Array {}')();
              const view = new Ctor(4);
              return {
                isFn: typeof Ctor,
                length: view.length,
                isView: ArrayBuffer.isView(view)
              };
            })();
        """);

        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal("function", obj["isFn"]);
        Assert.Equal(4d, obj["length"]);
        Assert.True(obj["isView"] as bool?);
    }
}

public class FastPathFunctionConstructorTests(ITestOutputHelper output) : FunctionConstructorTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class ReferenceFunctionConstructorTests(ITestOutputHelper output) : FunctionConstructorTestsBase(output)
{
    protected override bool EnableFastPaths => false;
}
