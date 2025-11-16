namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for tagged template literals.
/// </summary>
public class TaggedTemplateTests
{
    [Fact(Timeout = 2000)]
    public async Task TaggedTemplate_BasicFunction()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function myTag(strings, ...values) {
                                                           return strings[0] + values[0] + strings[1];
                                                       }
                                                       let name = "World";
                                                       myTag`Hello ${name}!`;

                                           """);
        Assert.Equal("Hello World!", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TaggedTemplate_MultipleSubstitutions()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function tag(strings, ...values) {
                                                           let result = "";
                                                           let i = 0;
                                                           while (i < strings.length) {
                                                               result = result + strings[i];
                                                               if (i < values.length) {
                                                                   result = result + "[" + values[i] + "]";
                                                               }
                                                               i = i + 1;
                                                           }
                                                           return result;
                                                       }
                                                       tag`a${1}b${2}c${3}d`;

                                           """);
        Assert.Equal("a[1]b[2]c[3]d", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TaggedTemplate_StringsArray()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function tag(strings) {
                                                           return strings.length;
                                                       }
                                                       tag`a${1}b${2}c`;

                                           """);
        Assert.Equal(3d, result); // ["a", "b", "c"]
    }

    [Fact(Timeout = 2000)]
    public async Task TaggedTemplate_NoSubstitutions()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function tag(strings) {
                                                           return strings[0];
                                                       }
                                                       tag`Hello, World!`;

                                           """);
        Assert.Equal("Hello, World!", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TaggedTemplate_WithExpressions()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function tag(strings, ...values) {
                                                           return values[0] + values[1];
                                                       }
                                                       tag`Sum: ${5 + 3} and ${10 * 2}`;

                                           """);
        Assert.Equal(28d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Raw_Basic()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       String.raw`Hello\nWorld`;

                                           """);
        Assert.Equal("Hello\\nWorld", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Raw_WithSubstitutions()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let name = "Alice";
                                                       String.raw`Line1\n${name}\tLine2`;

                                           """);
        Assert.Equal("Line1\\nAlice\\tLine2", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Raw_MultipleLines()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       String.raw`First\nSecond\rThird\tFourth`;

                                           """);
        Assert.Equal("First\\nSecond\\rThird\\tFourth", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TaggedTemplate_RawProperty()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function tag(strings) {
                                                           return strings.raw[0];
                                                       }
                                                       tag`Hello\nWorld`;

                                           """);
        Assert.Equal("Hello\\nWorld", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TaggedTemplate_CompareRawAndCooked()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function tag(strings) {
                                                           return strings[0] === strings.raw[0];
                                                       }
                                                       tag`Hello World`;

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task TaggedTemplate_AsMethodCall()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj = {
                                                           tag: function(strings, value) {
                                                               return strings[0] + value + strings[1];
                                                           }
                                                       };
                                                       obj.tag`Count: ${42}!`;

                                           """);
        Assert.Equal("Count: 42!", result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task TaggedTemplate_ChainedAccess()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function createTagFunction() {
                                                           return function(strings, value) {
                                                               return value * 2;
                                                           };
                                                       }
                                                       createTagFunction()`Value: ${21}`;

                                           """);
        Assert.Equal(42d, result);
    }
}
