using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for String prototype methods.
/// </summary>
public class StringMethodsTests
{
    [Fact]
    public void String_Length_Property()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello"";
            str.length;
        ");
        Assert.Equal(5d, result);
    }

    [Fact]
    public void String_CharAt()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello"";
            str.charAt(1);
        ");
        Assert.Equal("e", result);
    }

    [Fact]
    public void String_CharAt_OutOfBounds()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello"";
            str.charAt(10);
        ");
        Assert.Equal("", result);
    }

    [Fact]
    public void String_CharCodeAt()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello"";
            str.charCodeAt(0);
        ");
        Assert.Equal(104d, result); // 'h' = 104
    }

    [Fact]
    public void String_IndexOf()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            str.indexOf(""world"");
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void String_IndexOf_NotFound()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello"";
            str.indexOf(""xyz"");
        ");
        Assert.Equal(-1d, result);
    }

    [Fact]
    public void String_IndexOf_WithPosition()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello hello"";
            str.indexOf(""hello"", 1);
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void String_LastIndexOf()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world hello"";
            str.lastIndexOf(""hello"");
        ");
        Assert.Equal(12d, result);
    }

    [Fact]
    public void String_Substring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            str.substring(0, 5);
        ");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void String_Substring_OneArg()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            str.substring(6);
        ");
        Assert.Equal("world", result);
    }

    [Fact]
    public void String_Substring_SwapsIfStartGreaterThanEnd()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello"";
            str.substring(3, 1);
        ");
        Assert.Equal("el", result);
    }

    [Fact]
    public void String_Slice()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            str.slice(0, 5);
        ");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void String_Slice_NegativeIndices()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            str.slice(-5, -1);
        ");
        Assert.Equal("worl", result);
    }

    [Fact]
    public void String_ToLowerCase()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""HELLO World"";
            str.toLowerCase();
        ");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void String_ToUpperCase()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello World"";
            str.toUpperCase();
        ");
        Assert.Equal("HELLO WORLD", result);
    }

    [Fact]
    public void String_Trim()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""  hello world  "";
            str.trim();
        ");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void String_TrimStart()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""  hello  "";
            str.trimStart();
        ");
        Assert.Equal("hello  ", result);
    }

    [Fact]
    public void String_TrimEnd()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""  hello  "";
            str.trimEnd();
        ");
        Assert.Equal("  hello", result);
    }

    [Fact]
    public void String_Split()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""a,b,c"";
            let parts = str.split("","");
            parts[1];
        ");
        Assert.Equal("b", result);
    }

    [Fact]
    public void String_Split_WithLimit()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""a,b,c,d"";
            let parts = str.split("","", 2);
            parts.length;
        ");
        Assert.Equal(2d, result);
    }

    [Fact]
    public void String_Split_EmptySeparator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""abc"";
            let parts = str.split("""");
            parts.length;
        ");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void String_Replace()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            str.replace(""world"", ""there"");
        ");
        Assert.Equal("hello there", result);
    }

    [Fact]
    public void String_Replace_OnlyFirstOccurrence()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello hello"";
            str.replace(""hello"", ""hi"");
        ");
        Assert.Equal("hi hello", result);
    }

    [Fact]
    public void String_StartsWith()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            str.startsWith(""hello"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void String_StartsWith_WithPosition()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            str.startsWith(""world"", 6);
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void String_EndsWith()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            str.endsWith(""world"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void String_EndsWith_WithLength()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            str.endsWith(""hello"", 5);
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void String_Includes()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            str.includes(""lo wo"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void String_Includes_NotFound()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello"";
            str.includes(""xyz"");
        ");
        Assert.False((bool)result!);
    }

    [Fact]
    public void String_Repeat()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""abc"";
            str.repeat(3);
        ");
        Assert.Equal("abcabcabc", result);
    }

    [Fact]
    public void String_PadStart()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""5"";
            str.padStart(3, ""0"");
        ");
        Assert.Equal("005", result);
    }

    [Fact]
    public void String_PadEnd()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""5"";
            str.padEnd(3, ""0"");
        ");
        Assert.Equal("500", result);
    }

    [Fact]
    public void String_Chaining()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""  HELLO WORLD  "";
            str.trim().toLowerCase().replace(""world"", ""there"");
        ");
        Assert.Equal("hello there", result);
    }

    [Fact]
    public void String_Methods_InLoop()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let words = ""apple,banana,cherry"".split("","");
            let upperWords = """";
            let i = 0;
            while (i < words.length) {
                if (i > 0) {
                    upperWords = upperWords + "","";
                }
                upperWords = upperWords + words[i].toUpperCase();
                i = i + 1;
            }
            upperWords;
        ");
        Assert.Equal("APPLE,BANANA,CHERRY", result);
    }
}
