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

    [Fact]
    public async Task ArrayDestructuringAssignment_Works()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let first = 0;
            let second = 0;
            [first, second] = [3, 7];
            first * second;
        ");

        Assert.Equal(21.0, result);
    }

    [Fact]
    public async Task ObjectDestructuringAssignment_WithNestedPattern_Works()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let x = 0;
            let y = 0;
            ({ x, inner: { y } } = { x: 2, inner: { y: 5 } });
            x + y;
        ");

        Assert.Equal(7.0, result);
    }
}
