using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for Regular Expression support.
/// </summary>
[Category(TestCategories.StdLibRegExp)]
public sealed class RegExpTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task RegExp_Constructor_Basic()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = new RegExp("hello");
                                                       regex.source;

                                           """);
        Assert.Equal("hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_Constructor_WithFlags()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = new RegExp("hello", "i");
                                                       regex.ignoreCase;

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_Test_Matches()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = new RegExp("world");
                                                       regex.test("hello world");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_Test_NoMatch()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = new RegExp("xyz");
                                                       regex.test("hello world");

                                           """);
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_Test_CaseInsensitive()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = new RegExp("HELLO", "i");
                                                       regex.test("hello world");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_Exec_ReturnsMatchArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = new RegExp("world");
                                                       let match = regex.exec("hello world");
                                                       match[0];

                                           """);
        Assert.Equal("world", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_Exec_ReturnsIndex()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = new RegExp("world");
                                                       let match = regex.exec("hello world");
                                                       match.index;

                                           """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_Exec_WithCaptureGroups()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = new RegExp("([a-z]+)@([a-z]+)");
                                                       let match = regex.exec("user@example.com");
                                                       match[1];

                                           """);
        Assert.Equal("user", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_Exec_NoMatch_ReturnsNull()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = new RegExp("xyz");
                                                       regex.exec("hello world");

                                           """);
        Assert.Null(result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Match_WithRegExp()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       let regex = new RegExp("world");
                                                       let match = str.match(regex);
                                                       match[0];

                                           """);
        Assert.Equal("world", result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Match_GlobalFlag()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello hello hello";
                                                       let regex = new RegExp("hello", "g");
                                                       let matches = str.match(regex);
                                                       matches.length;

                                           """);
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Search_ReturnsIndex()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       let regex = new RegExp("world");
                                                       str.search(regex);

                                           """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Search_NoMatch()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello";
                                                       let regex = new RegExp("xyz");
                                                       str.search(regex);

                                           """);
        Assert.Equal(-1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task String_Replace_WithRegExp()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello world";
                                                       let regex = new RegExp("world");
                                                       str.replace(regex, "there");

                                           """);
        Assert.Equal("hello there", result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task String_Replace_GlobalFlag()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello hello hello";
                                                       let regex = new RegExp("hello", "g");
                                                       str.replace(regex, "hi");

                                           """);
        Assert.Equal("hi hi hi", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_GlobalFlag_Test_UpdatesLastIndex()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = new RegExp("o", "g");
                                                       let str = "hello world";
                                                       let found1 = regex.test(str);
                                                       let index1 = regex.lastIndex;
                                                       let found2 = regex.test(str);
                                                       let index2 = regex.lastIndex;
                                                       index1 + index2;

                                           """);
        // First 'o' at index 4, lastIndex becomes 5
        // Second 'o' at index 7, lastIndex becomes 8
        Assert.Equal(13d, result); // 5 + 8
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_Pattern_WithDigits()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = new RegExp("[0-9]+");
                                                       regex.test("abc123def");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_Pattern_EmailLike()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = new RegExp("[a-z]+@[a-z]+\.[a-z]+", "i");
                                                       regex.test("user@example.com");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_Multiline_Flag()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = new RegExp("^world", "m");
                                                       regex.multiline;

                                           """);
        Assert.True((bool)result!);
    }

    // Regex Literal Tests
    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_Basic()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /hello/;
                                                       regex.source;

                                           """);
        Assert.Equal("hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_WithFlags()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /hello/i;
                                                       regex.ignoreCase;

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_MultipleFlags()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("""

                                          let regex = /hello/gi;

                              """);
        var ignoreCase = await engine.Evaluate("regex.ignoreCase;");
        var global = await engine.Evaluate("regex.global;");
        Assert.True((bool)ignoreCase!);
        Assert.True((bool)global!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_Test()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /world/;
                                                       regex.test("hello world");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_TestCaseInsensitive()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /HELLO/i;
                                                       regex.test("hello world");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_Exec()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /world/;
                                                       let match = regex.exec("hello world");
                                                       match[0];

                                           """);
        Assert.Equal("world", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_WithEscapes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /\d+/;
                                                       regex.test("abc123");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_WithCharacterClass()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /[0-9]+/;
                                                       regex.test("abc123");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_InAssignment()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let pattern = /test/i;
                                                       pattern.test("Testing");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_InFunctionCall()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       function testPattern(regex) {
                                                           return regex.test("hello");
                                                       }
                                                       testPattern(/hello/);

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_InArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let patterns = [/hello/, /world/];
                                                       patterns[0].test("hello");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_InObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { pattern: /test/ };
                                                       obj.pattern.test("test");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_StringMatch()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = "I have 2 cats and 3 dogs";
                                                       let matches = str.match(/[0-9]+/g);
                                                       matches.length;

                                           """);
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_StringReplace()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = "hello hello hello";
                                                       str.replace(/hello/g, "hi");

                                           """);
        Assert.Equal("hi hi hi", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_StringSearch()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = "The year is 2024";
                                                       str.search(/[0-9]+/);

                                           """);
        Assert.Equal(12d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_ComplexPattern()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let emailPattern = /([a-z]+)@([a-z]+)\.([a-z]+)/i;
                                                       let match = emailPattern.exec("user@example.com");
                                                       match[1];

                                           """);
        Assert.Equal("user", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_AfterReturn()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       function getPattern() {
                                                           return /test/;
                                                       }
                                                       getPattern().test("test");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_AfterComma()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       function check(a, b) {
                                                           return b.test("hello");
                                                       }
                                                       check(1, /hello/);

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_EscapedSlash()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /\//;
                                                       regex.test("a/b");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_ComplexCharacterClass()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /[a-zA-Z0-9_]/;
                                                       regex.test("test_123");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_ComplexEscapeSequences()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = "test.*?";
                                                       str.replace(/\.\*\?$/, "*");

                                           """);
        Assert.Equal("test*", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_EscapeSequences_Dot()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /\./;
                                                       regex.test("test.txt");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_EscapeSequences_Star()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /\*/;
                                                       regex.test("2*3");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_EscapeSequences_Question()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /\?/;
                                                       regex.test("what?");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_EscapeSequences_Plus()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /\+/;
                                                       regex.test("1+2");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_MultipleEscapes_WithAnchors()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = "start.*?end";
                                                       str.replace(/^\w+\.\*\?\w+$/, "replaced");

                                           """);
        Assert.Equal("replaced", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_ParenthesesEscape()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /\(test\)/;
                                                       regex.test("(test)");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_BracketsEscape()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let regex = /\[test\]/;
                                                       regex.test("[test]");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_IncompleteHexEscape_ShouldTreatAsIdentityEscape()
    {
        // Per Annex B: Incomplete \x escape should match literal 'x'
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            /\x/.test("x")
        """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegexLiteral_IncompleteUnicodeEscape_ShouldTreatAsIdentityEscape()
    {
        // Per Annex B: Incomplete \u escape should match literal 'u'
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            /\u/.test("u")
        """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 10000)]
    public async Task RegexLiteral_LeadingBmpEscape_SourceRoundTripsWithoutCompiledRegexChurn()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            for (var cu = 0; cu <= 0x03ff; ++cu) {
              var eliminated =
                cu === 0x002A || cu === 0x002F || cu === 0x005C ||
                cu === 0x002B || cu === 0x003F || cu === 0x0028 ||
                cu === 0x0029 || cu === 0x005B || cu === 0x005D ||
                cu === 0x007B || cu === 0x007D;
              var lineTerminator = cu === 0x000A || cu === 0x000D;
              if (!eliminated && !lineTerminator) {
                var xx = "\\" + String.fromCharCode(cu);
                var pattern = eval("/" + xx + "/");
                if (pattern.source !== xx) {
                  throw new Error("Code unit: " + cu.toString(16));
                }
              }
            }
            true;
        """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_NamedGroups_SimpleMatch()
    {
        // "bab".match(/(?<a>a)/) should return ["a", "a"]
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var m = "bab".match(/(?<a>a)/);
            m !== null ? m[0] + "," + m[1] : "null";
        """);
        Assert.Equal("a,a", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_NamedGroups_MixedOrdering()
    {
        // .(?<a>a)(.) — JS expects: ["bab", "a", "b"]
        // .NET would give: ["bab", "b", "a"] without reorder map
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var m = "bab".match(/.(?<a>a)(.)/);
            m !== null ? m[0] + "," + m[1] + "," + m[2] : "null";
        """);
        Assert.Equal("bab,a,b", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_NamedGroups_AllNamed()
    {
        // .(?<a>a)(?<b>.) — all named, JS expects: ["bab", "a", "b"]
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var m = "bab".match(/.(?<a>a)(?<b>.)/);
            m !== null ? m[0] + "," + m[1] + "," + m[2] : "null";
        """);
        Assert.Equal("bab,a,b", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_NamedGroups_BackreferenceWithMixed()
    {
        // (.)(?<a>a)\1\2 on "baba" — JS expects: ["baba", "b", "a"]
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var m = "baba".match(/(.)(?<a>a)\1\2/);
            m !== null ? m[0] + "," + m[1] + "," + m[2] : "null";
        """);
        Assert.Equal("baba,b,a", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RegExp_NamedGroups_SelfReference()
    {
        // \k<a> inside (?<a>...) should match empty (group not yet captured)
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var m = /(?<a>\k<a>\w)../.exec("bab");
            m !== null ? m[0] + ":" + m[1] + ":" + m.groups.a : "null";
        """);
        Assert.Equal("bab:b:b", result);
    }

    [Fact(Timeout = 5000)]
    public async Task RegExp_NamedGroups_References()
    {
        // From non-unicode-references.js
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var errors = [];
            function check(name, expected, actual) {
                if (expected === null) {
                    if (actual !== null) errors.push(name + ": expected null got " + JSON.stringify(actual));
                    return;
                }
                if (actual === null || actual === undefined) {
                    errors.push(name + ": got null");
                    return;
                }
                if (Array.isArray(expected)) {
                    if (actual.length !== expected.length) {
                        errors.push(name + ": len " + actual.length + " vs " + expected.length);
                        return;
                    }
                    for (var i = 0; i < expected.length; i++) {
                        if (actual[i] !== expected[i]) {
                            errors.push(name + ": [" + i + "] " + JSON.stringify(actual[i]) + " vs " + JSON.stringify(expected[i]));
                            return;
                        }
                    }
                } else {
                    if (actual !== expected) errors.push(name + ": " + JSON.stringify(actual) + " vs " + JSON.stringify(expected));
                }
            }

            // Named references
            check("R1", ["bab", "b"], "bab".match(/(?<b>.).\k<b>/));
            check("R2", null, "baa".match(/(?<b>.).\k<b>/));

            // Reference inside group
            check("R3", ["bab", "b"], "bab".match(/(?<a>\k<a>\w)../));
            check("R4", "b", "bab".match(/(?<a>\k<a>\w)../) && "bab".match(/(?<a>\k<a>\w)../).groups.a);

            // Reference before group
            check("R5", ["bab", "b"], "bab".match(/\k<a>(?<a>b)\w\k<a>/));
            check("R6", "b", "bab".match(/\k<a>(?<a>b)\w\k<a>/) && "bab".match(/\k<a>(?<a>b)\w\k<a>/).groups.a);
            check("R7", ["bab", "b", "a"], "bab".match(/(?<b>b)\k<a>(?<a>a)\k<b>/));

            // Reference properties
            check("R8", "a", /(?<a>a)(?<b>b)\k<a>/.exec("aba") && /(?<a>a)(?<b>b)\k<a>/.exec("aba").groups.a);
            check("R9", "b", /(?<a>a)(?<b>b)\k<a>/.exec("aba") && /(?<a>a)(?<b>b)\k<a>/.exec("aba").groups.b);

            errors.length > 0 ? errors.join("; ") : "OK";
        """);
        Assert.Equal("OK", result);
    }

    [Fact(Timeout = 5000)]
    public async Task RegExp_NamedGroups_FullTest262Repro()
    {
        // Reproduces ALL lines from Test262 non-unicode-match.js
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var errors = [];
            function check(name, actual) {
                if (actual === null || actual === undefined) {
                    errors.push(name + ": null");
                    return;
                }
            }
            function compareArr(name, expected, actual) {
                if (actual === null || actual === undefined) {
                    errors.push(name + ": actual null");
                    return;
                }
                if (expected === null || expected === undefined) {
                    errors.push(name + ": expected null");
                    return;
                }
                if (actual.length !== expected.length) {
                    errors.push(name + ": length " + actual.length + " vs " + expected.length);
                    return;
                }
                for (var i = 0; i < expected.length; i++) {
                    if (actual[i] !== expected[i]) {
                        errors.push(name + ": [" + i + "] " + JSON.stringify(actual[i]) + " vs " + JSON.stringify(expected[i]));
                        return;
                    }
                }
            }
            // Part 1: compareArray with literal expected
            compareArr("L11", ["a", "a"], "bab".match(/(?<a>a)/));
            compareArr("L12", ["a", "a"], "bab".match(/(?<a42>a)/));
            compareArr("L13", ["a", "a"], "bab".match(/(?<_>a)/));
            compareArr("L14", ["a", "a"], "bab".match(/(?<$>a)/));
            compareArr("L15", ["bab", "a"], "bab".match(/.(?<$>a)./));
            compareArr("L16", ["bab", "a", "b"], "bab".match(/.(?<a>a)(.)/));
            compareArr("L17", ["bab", "a", "b"], "bab".match(/.(?<a>a)(?<b>.)/));
            compareArr("L18", ["bab", "ab"], "bab".match(/.(?<a>\w\w)/));
            compareArr("L19", ["bab", "bab"], "bab".match(/(?<a>\w\w\w)/));
            compareArr("L20", ["bab", "ba", "b"], "bab".match(/(?<a>\w\w)(?<b>\w)/));

            // Part 2: exec + groups
            var r = /(?<a>.)(?<b>.)(?<c>.)\k<c>\k<b>\k<a>/.exec("abccba");
            check("L21-exec", r);
            if (r) {
                var g = r.groups;
                if (g.a !== "a") errors.push("L21: a=" + g.a);
                if (g.b !== "b") errors.push("L21: b=" + g.b);
                if (g.c !== "c") errors.push("L21: c=" + g.c);
            }

            // Part 3: compareArray with match result as both args
            compareArr("L31", "bab".match(/(a)/), "bab".match(/(?<a>a)/));
            compareArr("L32", "bab".match(/(a)/), "bab".match(/(?<a42>a)/));
            compareArr("L33", "bab".match(/(a)/), "bab".match(/(?<_>a)/));
            compareArr("L34", "bab".match(/(a)/), "bab".match(/(?<$>a)/));
            compareArr("L35", "bab".match(/.(a)./), "bab".match(/.(?<$>a)./));
            compareArr("L36", "bab".match(/.(a)(.)/), "bab".match(/.(?<a>a)(.)/));
            compareArr("L37", "bab".match(/.(a)(.)/), "bab".match(/.(?<a>a)(?<b>.)/));
            compareArr("L38", "bab".match(/.(\w\w)/), "bab".match(/.(?<a>\w\w)/));
            compareArr("L39", "bab".match(/(\w\w\w)/), "bab".match(/(?<a>\w\w\w)/));
            compareArr("L40", "bab".match(/(\w\w)(\w)/), "bab".match(/(?<a>\w\w)(?<b>\w)/));

            // Part 4: backreferences
            compareArr("L41", ["bab", "b"], "bab".match(/(?<b>b).\1/));
            compareArr("L42", ["baba", "b", "a"], "baba".match(/(.)(?<a>a)\1\2/));
            compareArr("L43", ["baba", "b", "a", "b", "a"], "baba".match(/(.)(?<a>a)(?<b>\1)(\2)/));
            compareArr("L44", ["<a", "<"], "<a".match(/(?<lt><)a/));
            compareArr("L45", [">a", ">"], ">a".match(/(?<gt>>)a/));

            errors.length > 0 ? errors.join("; ") : "OK";
        """);
        Assert.Equal("OK", result);
    }

    [Fact(Timeout = 5000)]
    public async Task RegExp_DuplicateNamedGroups_Exec()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var errors = [];
            function check(label, expected, actual) {
                if (actual === null && expected !== null) { errors.push(label + ": got null"); return; }
                if (actual !== null && expected === null) { errors.push(label + ": expected null, got " + actual); return; }
                if (expected === null) return;
                if (actual.length !== expected.length) { errors.push(label + ": length " + actual.length + " != " + expected.length); return; }
                for (var i = 0; i < expected.length; i++) {
                    if (actual[i] !== expected[i]) { errors.push(label + ": [" + i + "] " + actual[i] + " != " + expected[i]); }
                }
            }

            check("T1", ["b", undefined, "b"], /(?<x>a)|(?<x>b)/.exec("bab"));
            check("T2", ["b", "b", undefined], /(?<x>b)|(?<x>a)/.exec("bab"));
            check("T3", ["aa", "a", undefined], /(?:(?<x>a)|(?<x>b))\k<x>/.exec("aa"));
            check("T4", ["bb", undefined, "b"], /(?:(?<x>a)|(?<x>b))\k<x>/.exec("bb"));
            check("T5", null, /(?:(?<x>a)|(?<x>b))\k<x>/.exec("abab"));

            var r6 = /(?:(?:(?<x>a)|(?<x>b))\k<x>){2}/.exec("aabb");
            check("T6", ["aabb", undefined, "b"], r6);
            if (r6 && r6.groups.x !== "b") errors.push("T6 groups.x=" + r6.groups.x);

            check("T7", null, /(?:(?:(?<x>a)|(?<x>b))\k<x>){2}/.exec("abab"));
            check("T8", null, /(?:(?<x>a)|(?<x>b))\k<x>/.exec("cdef"));

            check("T9", ["xx", "x", undefined], /^(?:(?<a>x)|(?<a>y)|z)\k<a>$/.exec("xx"));
            check("T10", ["z", undefined, undefined], /^(?:(?<a>x)|(?<a>y)|z)\k<a>$/.exec("z"));
            check("T11", null, /^(?:(?<a>x)|(?<a>y)|z)\k<a>$/.exec("zz"));
            check("T12", ["zy", undefined], /(?<a>x)|(?:zy\k<a>)/.exec("zy"));

            check("T13", ["xz", undefined, undefined], /^(?:(?<a>x)|(?<a>y)|z){2}\k<a>$/.exec("xz"));
            check("T14", ["yz", undefined, undefined], /^(?:(?<a>x)|(?<a>y)|z){2}\k<a>$/.exec("yz"));
            check("T15", null, /^(?:(?<a>x)|(?<a>y)|z){2}\k<a>$/.exec("xzx"));
            check("T16", null, /^(?:(?<a>x)|(?<a>y)|z){2}\k<a>$/.exec("yzy"));

            errors.length > 0 ? errors.join("; ") : "OK";
        """);
        Assert.Equal("OK", result);
    }

    [Fact(Timeout = 5000)]
    public async Task RegExp_DuplicateNamedGroups_Replace()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var errors = [];
            function eq(label, expected, actual) {
                if (actual !== expected) errors.push(label + ": " + JSON.stringify(actual) + " != " + JSON.stringify(expected));
            }

            eq("R1", "[a]b", "ab".replace(/(?<x>a)|(?<x>b)/, "[$<x>]"));
            eq("R2", "[b]a", "ba".replace(/(?<x>a)|(?<x>b)/, "[$<x>]"));
            eq("R3", "[a][a][]b", "ab".replace(/(?<x>a)|(?<x>b)/, "[$<x>][$1][$2]"));
            eq("R4", "[b][][b]a", "ba".replace(/(?<x>a)|(?<x>b)/, "[$<x>][$1][$2]"));
            eq("R5", "[a][b]", "ab".replace(/(?<x>a)|(?<x>b)/g, "[$<x>]"));
            eq("R6", "[b][a]", "ba".replace(/(?<x>a)|(?<x>b)/g, "[$<x>]"));

            errors.length > 0 ? errors.join("; ") : "OK";
        """);
        Assert.Equal("OK", result);
    }

    [Fact(Timeout = 5000)]
    public async Task RegExp_Lookbehind_BackreferencesToCaptures()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var errors = [];
            function check(label, expected, actual) {
                if (actual === null && expected !== null) { errors.push(label + ": got null"); return; }
                if (actual !== null && expected === null) { errors.push(label + ": expected null, got " + actual); return; }
                if (expected === null) return;
                if (actual.length !== expected.length) { errors.push(label + ": length " + actual.length + " != " + expected.length); return; }
                for (var i = 0; i < expected.length; i++) {
                    if (actual[i] !== expected[i]) { errors.push(label + ": [" + i + "] " + actual[i] + " != " + expected[i]); }
                }
            }

            check("#1", ["d", "C"], "abcCd".match(/(?<=\1(\w))d/i));
            check("#2", ["d", "x"], "abxxd".match(/(?<=\1([abx]))d/));
            check("#3", ["c", "ab"], "ababc".match(/(?<=\1(\w+))c/));
            check("#4", ["c", "b"], "ababbc".match(/(?<=\1(\w+))c/));
            check("#5", null, "ababdc".match(/(?<=\1(\w+))c/));
            check("#6", ["c", "abab"], "ababc".match(/(?<=(\w+)\1)c/));

            errors.length > 0 ? errors.join("; ") : "OK";
        """);
        Assert.Equal("OK", result);
    }

    [Fact(Timeout = 5000)]
    public async Task RegExp_Lookbehind_MutualRecursiveBackreferences()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var errors = [];
            function check(label, expected, actual) {
                if (actual === null && expected !== null) { errors.push(label + ": got null"); return; }
                if (actual !== null && expected === null) { errors.push(label + ": expected null, got " + actual); return; }
                if (expected === null) return;
                if (actual.length !== expected.length) { errors.push(label + ": length " + actual.length + " != " + expected.length); return; }
                for (var i = 0; i < expected.length; i++) {
                    if (actual[i] !== expected[i]) { errors.push(label + ": [" + i + "] " + actual[i] + " != " + expected[i]); }
                }
            }

            check("#1", ["cacb", "a", ""], /(?<=a(.\2)b(\1)).{4}/.exec("aabcacbc"));
            check("#2", ["b", "ac", "ac"], /(?<=a(\2)b(..\1))b/.exec("aacbacb"));
            check("#3", ["x", "aa"], /(?<=(?:\1b)(aa))./.exec("aabaax"));
            check("#4", ["x", "aa"], /(?<=(?:\1|b)(aa))./.exec("aaaax"));

            errors.length > 0 ? errors.join("; ") : "OK";
        """);
        Assert.Equal("OK", result);
    }
}
