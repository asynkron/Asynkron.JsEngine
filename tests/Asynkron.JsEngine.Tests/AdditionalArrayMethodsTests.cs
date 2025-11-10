using Xunit;

namespace Asynkron.JsEngine.Tests;

public class AdditionalArrayMethodsTests
{
    [Fact]
    public async Task Array_Fill_FillsWithValue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       arr.fill(0);
                                                       arr[2];
                                                   
                                           """);
        Assert.Equal(0d, result);
    }

    [Fact]
    public async Task Array_Fill_WithStartAndEnd()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       arr.fill(0, 2, 4);
                                                       arr[0] + arr[2] + arr[4];
                                                   
                                           """);
        Assert.Equal(1d + 0d + 5d, result);
    }

    [Fact]
    public async Task Array_Fill_WithNegativeIndices()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       arr.fill(0, -3, -1);
                                                       arr[2] + arr[3];
                                                   
                                           """);
        Assert.Equal(0d, result);
    }

    [Fact]
    public async Task Array_CopyWithin_CopiesElements()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       arr.copyWithin(0, 3);
                                                       arr[0] + arr[1];
                                                   
                                           """);
        Assert.Equal(4d + 5d, result);
    }

    [Fact]
    public async Task Array_CopyWithin_WithAllArguments()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       arr.copyWithin(1, 3, 4);
                                                       arr[1];
                                                   
                                           """);
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task Array_ToSorted_ReturnsSortedCopy()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [3, 1, 4, 1, 5];
                                                       let sorted = arr.toSorted(function(a, b) { return a - b; });
                                                       arr[0] + sorted[0];
                                                   
                                           """);
        Assert.Equal(3d + 1d, result); // original unchanged, sorted is [1,1,3,4,5]
    }

    [Fact]
    public async Task Array_ToReversed_ReturnsReversedCopy()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       let reversed = arr.toReversed();
                                                       arr[0] + reversed[0];
                                                   
                                           """);
        Assert.Equal(1d + 5d, result); // original unchanged
    }

    [Fact]
    public async Task Array_ToSpliced_ReturnsModifiedCopy()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       let spliced = arr.toSpliced(2, 2, 99);
                                                       arr.length + spliced.length + spliced[2];
                                                   
                                           """);
        Assert.Equal(5d + 4d + 99d, result); // original unchanged, spliced is [1,2,99,5]
    }

    [Fact]
    public async Task Array_With_ReplacesElement()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       let modified = arr.with(2, 99);
                                                       arr[2] + modified[2];
                                                   
                                           """);
        Assert.Equal(3d + 99d, result); // original unchanged
    }

    [Fact]
    public async Task Array_With_HandlesNegativeIndex()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       let modified = arr.with(-1, 99);
                                                       modified[4];
                                                   
                                           """);
        Assert.Equal(99d, result);
    }
}
