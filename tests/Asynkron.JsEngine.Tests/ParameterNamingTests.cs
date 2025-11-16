namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for parameter naming including contextual keywords
/// </summary>
public class ParameterNamingTests
{
    [Fact]
    public async Task FunctionParameter_Named_Set_ShouldWork()
    {
        await using var engine = new JsEngine();
        var code = @"
            function isInAstralSet(code, set) {
                return set[0];
            }
            isInAstralSet(100, [42]);
        ";

        var result = await engine.Evaluate(code);
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task FunctionParameter_Named_Get_ShouldWork()
    {
        await using var engine = new JsEngine();
        var code = @"
            function test(get, value) {
                return get + value;
            }
            test(10, 20);
        ";

        var result = await engine.Evaluate(code);
        Assert.Equal(30.0, result);
    }

    [Fact]
    public async Task ArrowFunction_Parameter_Named_Set_ShouldWork()
    {
        await using var engine = new JsEngine();
        var code = @"
            const fn = (code, set) => set[0];
            fn(100, [42]);
        ";

        var result = await engine.Evaluate(code);
        Assert.Equal(42.0, result);
    }

    // Additional tests to ensure getter/setter functionality still works
    [Fact]
    public async Task GetterSetter_InObjectLiteral_StillWorks()
    {
        await using var engine = new JsEngine();
        var code = @"
            var obj = {
                _value: 0,
                get value() { return this._value; },
                set value(v) { this._value = v; }
            };
            obj.value = 42;
            obj.value;
        ";

        var result = await engine.Evaluate(code);
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task GetterSetter_InClass_StillWorks()
    {
        await using var engine = new JsEngine();
        var code = @"
            class MyClass {
                constructor() { this._value = 0; }
                get value() { return this._value; }
                set value(v) { this._value = v; }
            }
            var obj = new MyClass();
            obj.value = 100;
            obj.value;
        ";

        var result = await engine.Evaluate(code);
        Assert.Equal(100.0, result);
    }

    [Fact]
    public async Task MixedUsage_GetSetAsParametersAndGetters()
    {
        await using var engine = new JsEngine();
        var code = @"
            // Function with 'set' as parameter name
            function processData(get, set) {
                return get + set;
            }

            // Object with getter/setter
            var obj = {
                _value: 0,
                get data() { return this._value; },
                set data(v) { this._value = v; }
            };

            // Use both
            obj.data = 10;
            processData(obj.data, 20);
        ";

        var result = await engine.Evaluate(code);
        Assert.Equal(30.0, result);
    }

    /// <summary>
    /// Test the exact example from the problem statement (simplified)
    /// </summary>
    [Fact]
    public async Task IsInAstralSet_ExactExample_FromProblemStatement()
    {
        await using var engine = new JsEngine();
        var code = @"
            // This is the function from the problem statement that was failing to parse
            function isInAstralSet(code, set) {
                // Simplified test - just return the first element of set
                return set.length > 0 ? set[0] : null;
            }

            // If this parses and runs, the fix is working
            var testSet = [42, 20, 30, 40];
            isInAstralSet(0x10015, testSet);
        ";

        var result = await engine.Evaluate(code);
        Assert.Equal(42.0, result);
    }
}
