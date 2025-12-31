using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Debug tests for prefix increment/decrement with valueOf.
/// Investigating S11.4.4_A2.2_T1 and S11.4.5_A2.2_T1 failures.
/// </summary>
public class PrefixIncrementDebugTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task PrefixIncrement_OnObjectWithValueOf_BasicCase()
    {
        await using var engine = CreateEngine();

        // The simplest case from S11.4.4_A2.2_T1
        var result = await engine.Evaluate(@"
            var object = {valueOf: function() {return 1}};
            ++object;
        ");

        Output.WriteLine($"Result: {result}");
        Assert.Equal(2.0, result);
    }

    [Fact]
    public async Task ValueOf_IsCalledCorrectly()
    {
        await using var engine = CreateEngine();

        // Check that valueOf is called
        var result = await engine.Evaluate(@"
            var called = false;
            var object = {valueOf: function() { called = true; return 1; }};
            Number(object); // This should call valueOf
            called;
        ");

        Output.WriteLine($"Result: {result}");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Number_WithObjectHavingValueOf()
    {
        await using var engine = CreateEngine();

        // Just call Number() on object with valueOf
        var result = await engine.Evaluate(@"
            var object = {valueOf: function() {return 1}};
            Number(object);
        ");

        Output.WriteLine($"Result: {result}");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public async Task UnaryPlus_WithObjectHavingValueOf()
    {
        await using var engine = CreateEngine();

        // Use unary plus which calls ToNumber
        var result = await engine.Evaluate(@"
            var object = {valueOf: function() {return 1}};
            +object;
        ");

        Output.WriteLine($"Result: {result}");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public async Task Addition_WithObjectHavingValueOf()
    {
        await using var engine = CreateEngine();

        // Use addition which calls ToPrimitive
        var result = await engine.Evaluate(@"
            var object = {valueOf: function() {return 1}};
            object + 1;
        ");

        Output.WriteLine($"Result: {result}");
        Assert.Equal(2.0, result);
    }

    [Fact]
    public async Task PostfixIncrement_OnObjectWithValueOf()
    {
        await using var engine = CreateEngine();

        // Test postfix increment
        var result = await engine.Evaluate(@"
            var object = {valueOf: function() {return 1}};
            object++;
        ");

        Output.WriteLine($"Result: {result}");
        Assert.Equal(1.0, result); // Postfix returns old value
    }

    [Fact]
    public async Task Variable_AfterPrefixIncrement()
    {
        await using var engine = CreateEngine();

        // Check what the variable contains after increment
        var result = await engine.Evaluate(@"
            var object = {valueOf: function() {return 1}};
            ++object;
            object;
        ");

        Output.WriteLine($"Result: {result}");
        Assert.Equal(2.0, result);
    }
}
