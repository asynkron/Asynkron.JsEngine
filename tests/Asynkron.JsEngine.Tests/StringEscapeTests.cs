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

    [Fact(Timeout = 2000)]
    public async Task LineContinuationInString()
    {
        var engine = new JsEngine();
        // This is a backslash followed by actual newline in the source - should be removed
        var result = await engine.Evaluate("let a = \"test\\\nline\"; a;");
        Assert.Equal("testline", result);
    }

    [Fact(Timeout = 2000)]
    public async Task LineContinuationWithEscapeSequence()
    {
        var engine = new JsEngine();
        // This has both \n (escape sequence) and \ followed by newline (line continuation)
        var result = await engine.Evaluate("let a = \"line1\\n\\\nline2\"; a;");
        Assert.Equal("line1\nline2", result);
    }

    [Fact(Timeout = 2000)]
    public async Task MultipleLineContinuations()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let a = \"start\\\n\\\n\\\nend\"; a;");
        Assert.Equal("startend", result);
    }

    [Fact(Timeout = 2000)]
    public async Task LineContinuationInSingleQuoteString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let a = 'test\\\nline'; a;");
        Assert.Equal("testline", result);
    }
}
