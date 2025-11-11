using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class StringEscapeTests
{
    [Fact(Timeout = 2000)]
    public async Task SimpleEscapedQuote()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"let a = ""test\""quote""; a;");
        Assert.Equal("test\"quote", result);
    }

    [Fact(Timeout = 2000)]
    public async Task SimpleEscapedBackslash()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"let a = ""test\\back""; a;");
        Assert.Equal("test\\back", result);
    }

    [Fact(Timeout = 2000)]
    public async Task SingleQuoteEscaped()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"let a = 'test\'quote'; a;");
        Assert.Equal("test'quote", result);
    }

    [Fact(Timeout = 2000)]
    public async Task NewlineEscape()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"let a = ""test\nline""; a;");
        Assert.Equal("test\nline", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexPattern()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"let a = '\\w+'; a;");
        Assert.Equal("\\w+", result);
    }

    [Fact(Timeout = 2000)]
    public async Task FunctionReturningString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"let e = function() { return '\\w+'; }; e();");
        Assert.Equal("\\w+", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ReplaceCall()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"''.replace(/^/,String)");
        Assert.NotNull(result);
    }

    [Fact(Timeout = 2000)]
    public async Task NotEmptyString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"!''");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ReturnWithoutSpace()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"function f() { return'test'; } f();");
        Assert.Equal("test", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ReturnEscapedStringWithoutSpace()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"function f() { return'\\w+'; } f();");
        Assert.Equal("\\w+", result);
    }
}
