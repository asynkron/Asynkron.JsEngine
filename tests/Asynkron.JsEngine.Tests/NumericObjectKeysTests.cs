using Xunit;

namespace Asynkron.JsEngine.Tests;

public class NumericObjectKeysTests
{
    [Fact(Timeout = 2000)]
    public async Task Should_Support_Numeric_Keys_In_Object_Literals()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var validation = {
                20: 2889.0000000000045,
                40: 2889.0000000000055,
                80: 2889.000000000005,
                160: 2889.0000000000055
            };
            validation[20];
        ");
        Assert.Equal(2889.0000000000045, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Should_Access_Numeric_Keys_With_String()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var obj = { 42: 'hello' };
            obj['42'];
        ");
        Assert.Equal("hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Should_Access_Numeric_Keys_With_Number()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var obj = { 42: 'hello' };
            obj[42];
        ");
        Assert.Equal("hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_Keys_Should_Return_Numeric_Keys_As_Strings()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var validation = {
                20: 1,
                40: 2,
                80: 3,
                160: 4
            };
            var keys = Object.keys(validation);
            keys[0] + ',' + keys[1] + ',' + keys[2] + ',' + keys[3];
        ");
        Assert.Equal("20,40,80,160", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Should_Support_Mixed_String_And_Numeric_Keys()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var obj = {
                name: 'test',
                42: 'answer',
                'key': 'value',
                100: 'hundred'
            };
            obj[42] + ' ' + obj.name;
        ");
        Assert.Equal("answer test", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Should_Support_Floating_Point_Keys()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var obj = { 3.14: 'pi' };
            obj[3.14];
        ");
        Assert.Equal("pi", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Should_Support_Zero_As_Key()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var obj = { 0: 'zero' };
            obj[0];
        ");
        Assert.Equal("zero", result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task Should_Support_Negative_Number_Keys_With_Computed_Property()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var obj = { [-5]: 'negative' };
            obj[-5];
        ");
        Assert.Equal("negative", result);
    }
}
