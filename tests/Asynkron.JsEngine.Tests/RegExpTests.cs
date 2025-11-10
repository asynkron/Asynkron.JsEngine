using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for Regular Expression support.
/// </summary>
public class RegExpTests
{
    [Fact]
    public async Task RegExp_Constructor_Basic()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = new RegExp(""hello"");
            regex.source;
        ");
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task RegExp_Constructor_WithFlags()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = new RegExp(""hello"", ""i"");
            regex.ignoreCase;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegExp_Test_Matches()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = new RegExp(""world"");
            regex.test(""hello world"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegExp_Test_NoMatch()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = new RegExp(""xyz"");
            regex.test(""hello world"");
        ");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task RegExp_Test_CaseInsensitive()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = new RegExp(""HELLO"", ""i"");
            regex.test(""hello world"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegExp_Exec_ReturnsMatchArray()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = new RegExp(""world"");
            let match = regex.exec(""hello world"");
            match[0];
        ");
        Assert.Equal("world", result);
    }

    [Fact]
    public async Task RegExp_Exec_ReturnsIndex()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = new RegExp(""world"");
            let match = regex.exec(""hello world"");
            match.index;
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task RegExp_Exec_WithCaptureGroups()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = new RegExp(""([a-z]+)@([a-z]+)"");
            let match = regex.exec(""user@example.com"");
            match[1];
        ");
        Assert.Equal("user", result);
    }

    [Fact]
    public async Task RegExp_Exec_NoMatch_ReturnsNull()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = new RegExp(""xyz"");
            regex.exec(""hello world"");
        ");
        Assert.Null(result);
    }

    [Fact]
    public async Task String_Match_WithRegExp()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let str = ""hello world"";
            let regex = new RegExp(""world"");
            let match = str.match(regex);
            match[0];
        ");
        Assert.Equal("world", result);
    }

    [Fact]
    public async Task String_Match_GlobalFlag()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let str = ""hello hello hello"";
            let regex = new RegExp(""hello"", ""g"");
            let matches = str.match(regex);
            matches.length;
        ");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task String_Search_ReturnsIndex()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let str = ""hello world"";
            let regex = new RegExp(""world"");
            str.search(regex);
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task String_Search_NoMatch()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let str = ""hello"";
            let regex = new RegExp(""xyz"");
            str.search(regex);
        ");
        Assert.Equal(-1d, result);
    }

    [Fact]
    public async Task String_Replace_WithRegExp()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let str = ""hello world"";
            let regex = new RegExp(""world"");
            str.replace(regex, ""there"");
        ");
        Assert.Equal("hello there", result);
    }

    [Fact]
    public async Task String_Replace_GlobalFlag()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let str = ""hello hello hello"";
            let regex = new RegExp(""hello"", ""g"");
            str.replace(regex, ""hi"");
        ");
        Assert.Equal("hi hi hi", result);
    }

    [Fact]
    public async Task RegExp_GlobalFlag_Test_UpdatesLastIndex()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
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
    public async Task RegExp_Pattern_WithDigits()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = new RegExp(""[0-9]+"");
            regex.test(""abc123def"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegExp_Pattern_EmailLike()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = new RegExp(""[a-z]+@[a-z]+\.[a-z]+"", ""i"");
            regex.test(""user@example.com"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegExp_Multiline_Flag()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = new RegExp(""^world"", ""m"");
            regex.multiline;
        ");
        Assert.True((bool)result!);
    }

    // Regex Literal Tests
    [Fact]
    public async Task RegexLiteral_Basic()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = /hello/;
            regex.source;
        ");
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task RegexLiteral_WithFlags()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = /hello/i;
            regex.ignoreCase;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegexLiteral_MultipleFlags()
    {
        var engine = new JsEngine();
        engine.EvaluateSync(@"
            let regex = /hello/gi;
        ");
        var ignoreCase = await engine.Evaluate("regex.ignoreCase;");
        var global = await engine.Evaluate("regex.global;");
        Assert.True((bool)ignoreCase!);
        Assert.True((bool)global!);
    }

    [Fact]
    public async Task RegexLiteral_Test()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = /world/;
            regex.test(""hello world"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegexLiteral_TestCaseInsensitive()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = /HELLO/i;
            regex.test(""hello world"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegexLiteral_Exec()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = /world/;
            let match = regex.exec(""hello world"");
            match[0];
        ");
        Assert.Equal("world", result);
    }

    [Fact]
    public async Task RegexLiteral_WithEscapes()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = /\d+/;
            regex.test(""abc123"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegexLiteral_WithCharacterClass()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = /[0-9]+/;
            regex.test(""abc123"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegexLiteral_InAssignment()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let pattern = /test/i;
            pattern.test(""Testing"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegexLiteral_InFunctionCall()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function testPattern(regex) {
                return regex.test(""hello"");
            }
            testPattern(/hello/);
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegexLiteral_InArray()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let patterns = [/hello/, /world/];
            patterns[0].test(""hello"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegexLiteral_InObject()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let obj = { pattern: /test/ };
            obj.pattern.test(""test"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegexLiteral_StringMatch()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let str = ""I have 2 cats and 3 dogs"";
            let matches = str.match(/[0-9]+/g);
            matches.length;
        ");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task RegexLiteral_StringReplace()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let str = ""hello hello hello"";
            str.replace(/hello/g, ""hi"");
        ");
        Assert.Equal("hi hi hi", result);
    }

    [Fact]
    public async Task RegexLiteral_StringSearch()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let str = ""The year is 2024"";
            str.search(/[0-9]+/);
        ");
        Assert.Equal(12d, result);
    }

    [Fact]
    public async Task RegexLiteral_ComplexPattern()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let emailPattern = /([a-z]+)@([a-z]+)\.([a-z]+)/i;
            let match = emailPattern.exec(""user@example.com"");
            match[1];
        ");
        Assert.Equal("user", result);
    }

    [Fact]
    public async Task RegexLiteral_AfterReturn()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function getPattern() {
                return /test/;
            }
            getPattern().test(""test"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegexLiteral_AfterComma()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function check(a, b) {
                return b.test(""hello"");
            }
            check(1, /hello/);
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegexLiteral_EscapedSlash()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = /\//;
            regex.test(""a/b"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task RegexLiteral_ComplexCharacterClass()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let regex = /[a-zA-Z0-9_]/;
            regex.test(""test_123"");
        ");
        Assert.True((bool)result!);
    }
}
