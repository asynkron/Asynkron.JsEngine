using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class SparseArrayTest(ITestOutputHelper output)
{
    [Fact]
    public async Task SparseArray_ReturnsUndefined_ForHoles()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var arr = [];
            arr[0] = 10;
            arr[5] = 50;

            var results = [];
            results.push('length=' + arr.length);
            results.push('arr[0]=' + arr[0]);
            results.push('arr[1]=' + arr[1]);
            results.push('arr[1]===undefined=' + (arr[1] === undefined));
            results.push('arr[5]=' + arr[5]);

            results.join(', ');
        ");

        output.WriteLine($"Result: {result}");
        Assert.Contains("length=6", result?.ToString());
        Assert.Contains("arr[1]===undefined=true", result?.ToString());
    }

    [Fact]
    public async Task SparseArray_WithOrEquals_CoercesUndefinedToZero()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var arr = [];
            arr[0] |= 50;
            arr[5] |= 100;

            var results = [];
            results.push('length=' + arr.length);
            results.push('arr[0]=' + arr[0]);
            results.push('arr[1]=' + arr[1]);
            results.push('arr[1]===undefined=' + (arr[1] === undefined));
            results.push('arr[5]=' + arr[5]);

            results.join(', ');
        ");

        output.WriteLine($"Result: {result}");
        Assert.Contains("length=6", result?.ToString());
        Assert.Contains("arr[0]=50", result?.ToString());
        Assert.Contains("arr[1]===undefined=true", result?.ToString());
        Assert.Contains("arr[5]=100", result?.ToString());
    }
}
