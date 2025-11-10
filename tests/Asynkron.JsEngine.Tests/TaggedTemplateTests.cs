using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for tagged template literals.
/// </summary>
public class TaggedTemplateTests
{
    [Fact]
    public async Task TaggedTemplate_BasicFunction()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            function myTag(strings, ...values) {
                return strings[0] + values[0] + strings[1];
            }
            let name = ""World"";
            myTag`Hello ${name}!`;
        ");
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public async Task TaggedTemplate_MultipleSubstitutions()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            function tag(strings, ...values) {
                let result = """";
                let i = 0;
                while (i < strings.length) {
                    result = result + strings[i];
                    if (i < values.length) {
                        result = result + ""["" + values[i] + ""]"";
                    }
                    i = i + 1;
                }
                return result;
            }
            tag`a${1}b${2}c${3}d`;
        ");
        Assert.Equal("a[1]b[2]c[3]d", result);
    }

    [Fact]
    public async Task TaggedTemplate_StringsArray()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            function tag(strings) {
                return strings.length;
            }
            tag`a${1}b${2}c`;
        ");
        Assert.Equal(3d, result); // ["a", "b", "c"]
    }

    [Fact]
    public async Task TaggedTemplate_NoSubstitutions()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            function tag(strings) {
                return strings[0];
            }
            tag`Hello, World!`;
        ");
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public async Task TaggedTemplate_WithExpressions()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            function tag(strings, ...values) {
                return values[0] + values[1];
            }
            tag`Sum: ${5 + 3} and ${10 * 2}`;
        ");
        Assert.Equal(28d, result);
    }

    [Fact]
    public async Task String_Raw_Basic()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            String.raw`Hello\nWorld`;
        ");
        Assert.Equal("Hello\\nWorld", result);
    }

    [Fact]
    public async Task String_Raw_WithSubstitutions()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let name = ""Alice"";
            String.raw`Line1\n${name}\tLine2`;
        ");
        Assert.Equal("Line1\\nAlice\\tLine2", result);
    }

    [Fact]
    public async Task String_Raw_MultipleLines()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            String.raw`First\nSecond\rThird\tFourth`;
        ");
        Assert.Equal("First\\nSecond\\rThird\\tFourth", result);
    }

    [Fact]
    public async Task TaggedTemplate_RawProperty()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            function tag(strings) {
                return strings.raw[0];
            }
            tag`Hello\nWorld`;
        ");
        Assert.Equal("Hello\\nWorld", result);
    }

    [Fact]
    public async Task TaggedTemplate_CompareRawAndCooked()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            function tag(strings) {
                return strings[0] === strings.raw[0];
            }
            tag`Hello World`;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task TaggedTemplate_AsMethodCall()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {
                tag: function(strings, value) {
                    return strings[0] + value + strings[1];
                }
            };
            obj.tag`Count: ${42}!`;
        ");
        Assert.Equal("Count: 42!", result);
    }

    [Fact]
    public async Task TaggedTemplate_ChainedAccess()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            function createTagFunction() {
                return function(strings, value) {
                    return value * 2;
                };
            }
            createTagFunction()`Value: ${21}`;
        ");
        Assert.Equal(42d, result);
    }
}
