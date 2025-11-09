using Xunit;

namespace Asynkron.JsEngine.Tests;

public class ErrorTypesTests
{
    [Fact]
    public void Error_CanBeCreated()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let err = new Error('test message');
            err.message;
        ");
        Assert.Equal("test message", result);
    }

    [Fact]
    public void Error_HasName()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let err = new Error('test');
            err.name;
        ");
        Assert.Equal("Error", result);
    }

    [Fact]
    public void Error_ToString_WithMessage()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let err = new Error('test message');
            err.toString();
        ");
        Assert.Equal("Error: test message", result);
    }

    [Fact]
    public void TypeError_CanBeCreated()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let err = new TypeError('type error message');
            err.name + ': ' + err.message;
        ");
        Assert.Equal("TypeError: type error message", result);
    }

    [Fact]
    public void TypeError_HasCorrectName()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let err = new TypeError('test');
            err.name;
        ");
        Assert.Equal("TypeError", result);
    }

    [Fact]
    public void RangeError_CanBeCreated()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let err = new RangeError('out of range');
            err.message;
        ");
        Assert.Equal("out of range", result);
    }

    [Fact]
    public void RangeError_HasCorrectName()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let err = new RangeError('test');
            err.name;
        ");
        Assert.Equal("RangeError", result);
    }

    [Fact]
    public void ReferenceError_CanBeCreated()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let err = new ReferenceError('reference not found');
            err.message;
        ");
        Assert.Equal("reference not found", result);
    }

    [Fact]
    public void ReferenceError_HasCorrectName()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let err = new ReferenceError('test');
            err.name;
        ");
        Assert.Equal("ReferenceError", result);
    }

    [Fact]
    public void SyntaxError_CanBeCreated()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let err = new SyntaxError('syntax issue');
            err.message;
        ");
        Assert.Equal("syntax issue", result);
    }

    [Fact]
    public void SyntaxError_HasCorrectName()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let err = new SyntaxError('test');
            err.name;
        ");
        Assert.Equal("SyntaxError", result);
    }

    [Fact]
    public void Error_WithNoMessage()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let err = new Error();
            err.message;
        ");
        Assert.Equal("", result);
    }

    [Fact]
    public void Error_ToString_WithNoMessage()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let err = new TypeError();
            err.toString();
        ");
        Assert.Equal("TypeError", result);
    }
}
