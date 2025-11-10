using Xunit;

namespace Asynkron.JsEngine.Tests;

public class ErrorTypesTests
{
    [Fact(Timeout = 2000)]
    public async Task Error_CanBeCreated()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let err = new Error('test message');
                                                       err.message;
                                                   
                                           """);
        Assert.Equal("test message", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Error_HasName()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let err = new Error('test');
                                                       err.name;
                                                   
                                           """);
        Assert.Equal("Error", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Error_ToString_WithMessage()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let err = new Error('test message');
                                                       err.toString();
                                                   
                                           """);
        Assert.Equal("Error: test message", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeError_CanBeCreated()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let err = new TypeError('type error message');
                                                       err.name + ': ' + err.message;
                                                   
                                           """);
        Assert.Equal("TypeError: type error message", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeError_HasCorrectName()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let err = new TypeError('test');
                                                       err.name;
                                                   
                                           """);
        Assert.Equal("TypeError", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RangeError_CanBeCreated()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let err = new RangeError('out of range');
                                                       err.message;
                                                   
                                           """);
        Assert.Equal("out of range", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RangeError_HasCorrectName()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let err = new RangeError('test');
                                                       err.name;
                                                   
                                           """);
        Assert.Equal("RangeError", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ReferenceError_CanBeCreated()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let err = new ReferenceError('reference not found');
                                                       err.message;
                                                   
                                           """);
        Assert.Equal("reference not found", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ReferenceError_HasCorrectName()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let err = new ReferenceError('test');
                                                       err.name;
                                                   
                                           """);
        Assert.Equal("ReferenceError", result);
    }

    [Fact(Timeout = 2000)]
    public async Task SyntaxError_CanBeCreated()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let err = new SyntaxError('syntax issue');
                                                       err.message;
                                                   
                                           """);
        Assert.Equal("syntax issue", result);
    }

    [Fact(Timeout = 2000)]
    public async Task SyntaxError_HasCorrectName()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let err = new SyntaxError('test');
                                                       err.name;
                                                   
                                           """);
        Assert.Equal("SyntaxError", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Error_WithNoMessage()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let err = new Error();
                                                       err.message;
                                                   
                                           """);
        Assert.Equal("", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Error_ToString_WithNoMessage()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let err = new TypeError();
                                                       err.toString();
                                                   
                                           """);
        Assert.Equal("TypeError", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeError_CanBeThrown()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let caught = null;
                                                       try {
                                                           throw new TypeError('invalid type');
                                                       } catch (e) {
                                                           caught = e;
                                                       }
                                                       caught.name + ': ' + caught.message;
                                                   
                                           """);
        Assert.Equal("TypeError: invalid type", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RangeError_CanBeThrown()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let caught = null;
                                                       try {
                                                           throw new RangeError('value out of range');
                                                       } catch (e) {
                                                           caught = e;
                                                       }
                                                       caught.name + ': ' + caught.message;
                                                   
                                           """);
        Assert.Equal("RangeError: value out of range", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ReferenceError_CanBeThrown()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let caught = null;
                                                       try {
                                                           throw new ReferenceError('undefined reference');
                                                       } catch (e) {
                                                           caught = e;
                                                       }
                                                       caught.name + ': ' + caught.message;
                                                   
                                           """);
        Assert.Equal("ReferenceError: undefined reference", result);
    }

    [Fact(Timeout = 2000)]
    public async Task SyntaxError_CanBeThrown()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let caught = null;
                                                       try {
                                                           throw new SyntaxError('bad syntax');
                                                       } catch (e) {
                                                           caught = e;
                                                       }
                                                       caught.name + ': ' + caught.message;
                                                   
                                           """);
        Assert.Equal("SyntaxError: bad syntax", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Error_PreservesPropertiesWhenCaught()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let caught = null;
                                                       try {
                                                           let err = new TypeError('test error');
                                                           err.customProp = 'custom value';
                                                           throw err;
                                                       } catch (e) {
                                                           caught = e;
                                                       }
                                                       caught.customProp;
                                                   
                                           """);
        Assert.Equal("custom value", result);
    }

    [Fact(Timeout = 2000)]
    public async Task MultipleErrorTypes_CanBeDistinguished()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let results = [];
                                                       
                                                       try {
                                                           throw new TypeError('type');
                                                       } catch (e) {
                                                           results.push(e.name);
                                                       }
                                                       
                                                       try {
                                                           throw new RangeError('range');
                                                       } catch (e) {
                                                           results.push(e.name);
                                                       }
                                                       
                                                       try {
                                                           throw new ReferenceError('reference');
                                                       } catch (e) {
                                                           results.push(e.name);
                                                       }
                                                       
                                                       results.join(',');
                                                   
                                           """);
        Assert.Equal("TypeError,RangeError,ReferenceError", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Error_CanBeRethrown()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let caught = null;
                                                       try {
                                                           try {
                                                               throw new TypeError('original');
                                                           } catch (e) {
                                                               throw e;
                                                           }
                                                       } catch (e) {
                                                           caught = e;
                                                       }
                                                       caught.name + ': ' + caught.message;
                                                   
                                           """);
        Assert.Equal("TypeError: original", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Error_InFunctionCall()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function throwsError() {
                                                           throw new RangeError('function error');
                                                       }
                                                       
                                                       let caught = null;
                                                       try {
                                                           throwsError();
                                                       } catch (e) {
                                                           caught = e;
                                                       }
                                                       caught.name + ': ' + caught.message;
                                                   
                                           """);
        Assert.Equal("RangeError: function error", result);
    }
}
