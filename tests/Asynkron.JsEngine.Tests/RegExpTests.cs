using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for Regular Expression support.
/// </summary>
public class RegExpTests
{
    [Fact]
    public void RegExp_Constructor_Basic()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let regex = new RegExp(""hello"");
            regex.source;
        ");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void RegExp_Constructor_WithFlags()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let regex = new RegExp(""hello"", ""i"");
            regex.ignoreCase;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void RegExp_Test_Matches()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let regex = new RegExp(""world"");
            regex.test(""hello world"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void RegExp_Test_NoMatch()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let regex = new RegExp(""xyz"");
            regex.test(""hello world"");
        ");
        Assert.False((bool)result!);
    }

    [Fact]
    public void RegExp_Test_CaseInsensitive()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let regex = new RegExp(""HELLO"", ""i"");
            regex.test(""hello world"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void RegExp_Exec_ReturnsMatchArray()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let regex = new RegExp(""world"");
            let match = regex.exec(""hello world"");
            match[0];
        ");
        Assert.Equal("world", result);
    }

    [Fact]
    public void RegExp_Exec_ReturnsIndex()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let regex = new RegExp(""world"");
            let match = regex.exec(""hello world"");
            match.index;
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void RegExp_Exec_WithCaptureGroups()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let regex = new RegExp(""([a-z]+)@([a-z]+)"");
            let match = regex.exec(""user@example.com"");
            match[1];
        ");
        Assert.Equal("user", result);
    }

    [Fact]
    public void RegExp_Exec_NoMatch_ReturnsNull()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let regex = new RegExp(""xyz"");
            regex.exec(""hello world"");
        ");
        Assert.Null(result);
    }

    [Fact]
    public void String_Match_WithRegExp()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            let regex = new RegExp(""world"");
            let match = str.match(regex);
            match[0];
        ");
        Assert.Equal("world", result);
    }

    [Fact]
    public void String_Match_GlobalFlag()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello hello hello"";
            let regex = new RegExp(""hello"", ""g"");
            let matches = str.match(regex);
            matches.length;
        ");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void String_Search_ReturnsIndex()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            let regex = new RegExp(""world"");
            str.search(regex);
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void String_Search_NoMatch()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello"";
            let regex = new RegExp(""xyz"");
            str.search(regex);
        ");
        Assert.Equal(-1d, result);
    }

    [Fact]
    public void String_Replace_WithRegExp()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello world"";
            let regex = new RegExp(""world"");
            str.replace(regex, ""there"");
        ");
        Assert.Equal("hello there", result);
    }

    [Fact]
    public void String_Replace_GlobalFlag()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = ""hello hello hello"";
            let regex = new RegExp(""hello"", ""g"");
            str.replace(regex, ""hi"");
        ");
        Assert.Equal("hi hi hi", result);
    }

    [Fact]
    public void RegExp_GlobalFlag_Test_UpdatesLastIndex()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let regex = new RegExp(""o"", ""g"");
            let str = ""hello world"";
            let found1 = regex.test(str);
            let index1 = regex.lastIndex;
            let found2 = regex.test(str);
            let index2 = regex.lastIndex;
            index1 + index2;
        ");
        // First 'o' at index 4, lastIndex becomes 5
        // Second 'o' at index 7, lastIndex becomes 8
        Assert.Equal(13d, result); // 5 + 8
    }

    [Fact]
    public void RegExp_Pattern_WithDigits()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let regex = new RegExp(""[0-9]+"");
            regex.test(""abc123def"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void RegExp_Pattern_EmailLike()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let regex = new RegExp(""[a-z]+@[a-z]+\.[a-z]+"", ""i"");
            regex.test(""user@example.com"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void RegExp_Multiline_Flag()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let regex = new RegExp(""^world"", ""m"");
            regex.multiline;
        ");
        Assert.True((bool)result!);
    }
}
