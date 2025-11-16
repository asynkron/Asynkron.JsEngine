using System.Threading.Tasks;
using Asynkron.JsEngine;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class TypedAstDestructuringTests
{
    [Fact]
    public async Task ArrayDestructuring_WithDefaultValue_Works()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let [a, b = 2] = [1];
            a + b;
        ");

        Assert.Equal(3.0, result);
    }

    [Fact]
    public async Task ObjectDestructuring_WithNestedPattern_Works()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let { x, inner: { z = 5 } } = { x: 1, inner: {} };
            x + z;
        ");

        Assert.Equal(6.0, result);
    }

    [Fact]
    public async Task ForLoop_WithInnerDestructuring_Works()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let total = 0;
            for (const pair of [[1, 2], [3, 4]]) {
                const [x, y] = pair;
                total += x + y;
            }
            total;
        ");

        Assert.Equal(10.0, result);
    }

    [Fact]
    public async Task FunctionParameter_DestructuringBinding_Works()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function combine({ left, right: { value } }) {
                return left + value;
            }
            combine({ left: 4, right: { value: 8 } });
        ");

        Assert.Equal(12.0, result);
    }
}
