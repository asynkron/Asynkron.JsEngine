using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class StringWellFormedTests(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task IsWellFormed_ReturnsTrueForWellFormedString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'hello'.isWellFormed();");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task IsWellFormed_ReturnsFalseForLoneHighSurrogate()
    {
        await using var engine = CreateEngine();
        // High surrogate (0xD800) without low surrogate
        var result = await engine.Evaluate("'\\uD800'.isWellFormed();");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task IsWellFormed_ReturnsFalseForLoneLowSurrogate()
    {
        await using var engine = CreateEngine();
        // Low surrogate (0xDC00) without high surrogate
        var result = await engine.Evaluate("'\\uDC00'.isWellFormed();");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task IsWellFormed_ReturnsTrueForValidSurrogatePair()
    {
        await using var engine = CreateEngine();
        // Valid surrogate pair (U+10000 = 0xD800 0xDC00)
        var result = await engine.Evaluate("'\\uD800\\uDC00'.isWellFormed();");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task ToWellFormed_ReplacesLoneHighSurrogate()
    {
        await using var engine = CreateEngine();
        // High surrogate (0xD800) should be replaced with U+FFFD
        var result = await engine.Evaluate("'abc\\uD800def'.toWellFormed();");
        Assert.Equal("abc\uFFFDdef", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ToWellFormed_ReplacesLoneLowSurrogate()
    {
        await using var engine = CreateEngine();
        // Low surrogate (0xDC00) should be replaced with U+FFFD
        var result = await engine.Evaluate("'abc\\uDC00def'.toWellFormed();");
        Assert.Equal("abc\uFFFDdef", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ToWellFormed_PreservesValidSurrogatePair()
    {
        await using var engine = CreateEngine();
        // Valid surrogate pair should be preserved
        var result = await engine.Evaluate("'abc\\uD800\\uDC00def'.toWellFormed();");
        Assert.Equal("abc\uD800\uDC00def", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ToWellFormed_ReturnsUnchangedForWellFormedString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'hello world'.toWellFormed();");
        Assert.Equal("hello world", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ToLocaleLowerCase_ConvertsToLowerCase()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'HELLO'.toLocaleLowerCase();");
        Assert.Equal("hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ToLocaleLowerCase_WithLocaleParameter()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'HELLO'.toLocaleLowerCase('en-US');");
        Assert.Equal("hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ToLocaleUpperCase_ConvertsToUpperCase()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'hello'.toLocaleUpperCase();");
        Assert.Equal("HELLO", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ToLocaleUpperCase_WithLocaleParameter()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'hello'.toLocaleUpperCase('en-US');");
        Assert.Equal("HELLO", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ToLocaleLowerCase_HandlesInvalidLocale()
    {
        await using var engine = CreateEngine();
        // Should fall back to invariant culture for invalid locale
        var result = await engine.Evaluate("'HELLO'.toLocaleLowerCase('invalid-locale');");
        Assert.Equal("hello", result);
    }
}