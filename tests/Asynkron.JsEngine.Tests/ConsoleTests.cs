using Asynkron.JsEngine.Lisp;

namespace Asynkron.JsEngine.Tests;

public class ConsoleTests
{
    [Fact]
    public async Task Console_Log_Should_Be_Available()
    {
        await using var engine = new JsEngine();

        // This should not throw "Undefined symbol 'console'"
        var result = await engine.Evaluate("console.log('Hello, World!')");

        // console.log should return undefined
        Assert.True(ReferenceEquals(result, JsSymbols.Undefined));
    }

    [Fact]
    public async Task Console_Log_Multiple_Arguments()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate(@"
            console.log('Hello', 42, true, null, undefined);
        ");

        Assert.True(ReferenceEquals(result, JsSymbols.Undefined));
    }

    [Fact]
    public async Task Console_Error_Should_Be_Available()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("console.error('Error message')");

        Assert.True(ReferenceEquals(result, JsSymbols.Undefined));
    }

    [Fact]
    public async Task Console_Warn_Should_Be_Available()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("console.warn('Warning message')");

        Assert.True(ReferenceEquals(result, JsSymbols.Undefined));
    }

    [Fact]
    public async Task Console_Info_Should_Be_Available()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("console.info('Info message')");

        Assert.True(ReferenceEquals(result, JsSymbols.Undefined));
    }

    [Fact]
    public async Task Console_Debug_Should_Be_Available()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("console.debug('Debug message')");

        Assert.True(ReferenceEquals(result, JsSymbols.Undefined));
    }

    [Fact]
    public async Task Console_Log_With_Objects()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate(@"
            let obj = { name: 'John', age: 30 };
            console.log('User:', obj);
        ");

        Assert.True(ReferenceEquals(result, JsSymbols.Undefined));
    }

    [Fact]
    public async Task Console_Log_With_Arrays()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate(@"
            let arr = [1, 2, 3, 4, 5];
            console.log('Numbers:', arr);
        ");

        Assert.True(ReferenceEquals(result, JsSymbols.Undefined));
    }

    [Fact]
    public async Task Console_Object_Should_Be_Accessible()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("typeof console");

        Assert.Equal("object", result);
    }

    [Fact]
    public async Task Console_Methods_Should_Be_Functions()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate(@"
            typeof console.log === 'function' &&
            typeof console.error === 'function' &&
            typeof console.warn === 'function' &&
            typeof console.info === 'function' &&
            typeof console.debug === 'function'
        ");

        Assert.True((bool)result!);
    }
}
