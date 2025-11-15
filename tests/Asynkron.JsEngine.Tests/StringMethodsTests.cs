using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for String prototype methods.
/// </summary>
public class StringMethodsTests
{
    [Fact(Timeout = 2000)]
    public async Task String_Length_Property()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello";
                                                       str.length;

                                           """);
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_CharAt()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello";
                                                       str.charAt(1);

                                           """);
        Assert.Equal("e", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_CharAt_OutOfBounds()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello";
                                                       str.charAt(10);

                                           """);
        Assert.Equal("", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_CharCodeAt()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello";
                                                       str.charCodeAt(0);

                                           """);
        Assert.Equal(104d, result); // 'h' = 104
    }

    [Fact(Timeout = 2000)]
    public async Task String_IndexOf()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       str.indexOf("world");

                                           """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_IndexOf_NotFound()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello";
                                                       str.indexOf("xyz");

                                           """);
        Assert.Equal(-1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_IndexOf_WithPosition()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello hello";
                                                       str.indexOf("hello", 1);

                                           """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_LastIndexOf()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world hello";
                                                       str.lastIndexOf("hello");

                                           """);
        Assert.Equal(12d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Substring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       str.substring(0, 5);

                                           """);
        Assert.Equal("hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Substring_OneArg()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       str.substring(6);

                                           """);
        Assert.Equal("world", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Substring_SwapsIfStartGreaterThanEnd()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello";
                                                       str.substring(3, 1);

                                           """);
        Assert.Equal("el", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Slice()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       str.slice(0, 5);

                                           """);
        Assert.Equal("hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Slice_NegativeIndices()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       str.slice(-5, -1);

                                           """);
        Assert.Equal("worl", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_ToLowerCase()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "HELLO World";
                                                       str.toLowerCase();

                                           """);
        Assert.Equal("hello world", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_ToUpperCase()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello World";
                                                       str.toUpperCase();

                                           """);
        Assert.Equal("HELLO WORLD", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Trim()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "  hello world  ";
                                                       str.trim();

                                           """);
        Assert.Equal("hello world", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_TrimStart()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "  hello  ";
                                                       str.trimStart();

                                           """);
        Assert.Equal("hello  ", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_TrimEnd()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "  hello  ";
                                                       str.trimEnd();

                                           """);
        Assert.Equal("  hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Split()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "a,b,c";
                                                       let parts = str.split(",");
                                                       parts[1];

                                           """);
        Assert.Equal("b", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Split_WithLimit()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "a,b,c,d";
                                                       let parts = str.split(",", 2);
                                                       parts.length;

                                           """);
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Split_EmptySeparator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "abc";
                                                       let parts = str.split("");
                                                       parts.length;

                                           """);
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Replace()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       str.replace("world", "there");

                                           """);
        Assert.Equal("hello there", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Replace_OnlyFirstOccurrence()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello hello";
                                                       str.replace("hello", "hi");

                                           """);
        Assert.Equal("hi hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_StartsWith()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       str.startsWith("hello");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task String_StartsWith_WithPosition()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       str.startsWith("world", 6);

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task String_EndsWith()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       str.endsWith("world");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task String_EndsWith_WithLength()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       str.endsWith("hello", 5);

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Includes()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       str.includes("lo wo");

                                           """);
        Assert.True((bool)result!);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task String_Includes_NotFound()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello";
                                                       str.includes("xyz");

                                           """);
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Repeat()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "abc";
                                                       str.repeat(3);

                                           """);
        Assert.Equal("abcabcabc", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_PadStart()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "5";
                                                       str.padStart(3, "0");

                                           """);
        Assert.Equal("005", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_PadEnd()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "5";
                                                       str.padEnd(3, "0");

                                           """);
        Assert.Equal("500", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Chaining()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "  HELLO WORLD  ";
                                                       str.trim().toLowerCase().replace("world", "there");

                                           """);
        Assert.Equal("hello there", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Methods_InLoop()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let words = "apple,banana,cherry".split(",");
                                                       let upperWords = "";
                                                       let i = 0;
                                                       while (i < words.length) {
                                                           if (i > 0) {
                                                               upperWords = upperWords + ",";
                                                           }
                                                           upperWords = upperWords + words[i].toUpperCase();
                                                           i = i + 1;
                                                       }
                                                       upperWords;

                                           """);
        Assert.Equal("APPLE,BANANA,CHERRY", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_CodePointAt()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello";
                                                       str.codePointAt(0);

                                           """);
        Assert.Equal(104d, result); // 'h' = 104
    }

    [Fact(Timeout = 2000)]
    public async Task String_CodePointAt_WithSurrogatePair()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "𝟘𝟙𝟚"; // Mathematical bold digits
                                                       str.codePointAt(0);

                                           """);
        Assert.Equal(120792d, result); // U+1D7D8
    }

    [Fact(Timeout = 2000)]
    public async Task String_LocaleCompare()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let a = "apple";
                                                       let b = "banana";
                                                       a.localeCompare(b) < 0;

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Normalize()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "café";
                                                       str.normalize("NFC").length;

                                           """);
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_MatchAll()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "test1test2test3";
                                                       let regex = /test\d/g;
                                                       let matches = str.matchAll(regex);
                                                       matches.length;

                                           """);
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Anchor()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello";
                                                       str.anchor("greeting");

                                           """);
        Assert.Equal("<a name=\"greeting\">hello</a>", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Link()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let str = "click here";
                                                       str.link("https://example.com");

                                           """);
        Assert.Equal("<a href=\"https://example.com\">click here</a>", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_FromCodePoint()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       String.fromCodePoint(65, 66, 67);

                                           """);
        Assert.Equal("ABC", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_FromCodePoint_WithSurrogatePairs()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       String.fromCodePoint(128512); // Grinning face emoji (0x1F600)

                                           """);
        Assert.Equal("😀", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_FromCharCode()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       String.fromCharCode(72, 101, 108, 108, 111);

                                           """);
        Assert.Equal("Hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Constructor()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       String(123);

                                           """);
        Assert.Equal("123", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Constructor_WithBoolean()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       String(true);

                                           """);
        Assert.Equal("true", result);
    }
}
