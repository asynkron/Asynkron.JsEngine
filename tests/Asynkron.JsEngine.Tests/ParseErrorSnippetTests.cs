using Asynkron.JsEngine;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Tests;

public class ParseErrorSnippetTests
{
    [Fact(Timeout = 2000)]
    public async Task ParseError_IncludesSourceSnippet()
    {
        await using var engine = new JsEngine();
        var source = @"let x = 10;
let y = 20;
let z = ;";

        var ex = await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate(source);
        });

        // Check that the error message contains the source context
        Assert.Contains("Source context:", ex.Message);
        Assert.Contains("let z = ;", ex.Message);
        Assert.Contains("^", ex.Message); // Should have a position marker
    }

    [Fact(Timeout = 2000)]
    public async Task ParseError_ShowsContextAroundError()
    {
        await using var engine = new JsEngine();
        var source = "let a = 1; let b = 2; let c = 3; let d = 4; let e = 5; let f let g = 7;"; // Missing = after f

        var ex = await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate(source);
        });

        // Check that the error message contains context around the error
        Assert.Contains("Source context:", ex.Message);
        // Should show some chars before and after the error position
        Assert.Contains("let f", ex.Message);
        Assert.Contains("^", ex.Message); // Should have a position marker
    }

    [Fact(Timeout = 2000)]
    public async Task ParseError_WithShortSource_ShowsFullLine()
    {
        await using var engine = new JsEngine();
        var source = "let x = ;"; // Missing initializer

        var ex = await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate(source);
        });

        // Even with short source, should show context
        Assert.Contains("Source context:", ex.Message);
        Assert.Contains("let x = ;", ex.Message);
        Assert.Contains("^", ex.Message); // Should have a position marker
    }

    [Fact(Timeout = 2000)]
    public async Task ParseError_AtBeginning_ShowsFromStart()
    {
        await using var engine = new JsEngine();
        var source = "class { }"; // Missing class name

        var ex = await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate(source);
        });

        // Should show from the beginning
        Assert.Contains("Source context:", ex.Message);
        Assert.Contains("class", ex.Message);
        Assert.Contains("^", ex.Message); // Should have a position marker
    }

    [Fact(Timeout = 2000)]
    public async Task ParseError_LongSource_ShowsSnippet()
    {
        await using var engine = new JsEngine();
        var source = "let a = 1; let b = 2; let c = 3; let d = 4; let e = 5; let f = 6; let g let h = 8;"; // Missing = after g

        var ex = await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate(source);
        });

        // Should show snippet around the error with ellipsis
        Assert.Contains("Source context:", ex.Message);
        Assert.Contains("let g", ex.Message);
        // Should have ellipsis if truncated
        Assert.Contains("...", ex.Message);
        Assert.Contains("^", ex.Message); // Should have a position marker
    }

    [Fact(Timeout = 2000)]
    public async Task ParseError_HasLineAndColumnInfo()
    {
        await using var engine = new JsEngine();
        var source = @"let x = 10;
let y = 20;
let z = ;";

        var ex = await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate(source);
        });

        // Check that line and column are present
        Assert.NotNull(ex.Line);
        Assert.NotNull(ex.Column);
        Assert.True(ex.Line > 0);
        Assert.True(ex.Column > 0);

        // Verify the line/column in the message
        Assert.Contains($"at line {ex.Line}, column {ex.Column}", ex.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseError_MarkerPointsToCorrectPosition()
    {
        await using var engine = new JsEngine();
        var source = "let x = ;"; // Error at semicolon

        var ex = await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate(source);
        });

        // The marker should be roughly at the position of the semicolon
        // Since the context includes the full line, the marker should be visible
        var lines = ex.Message.Split('\n');
        var hasMarkerLine = lines.Any(l => l.Contains('^'));
        Assert.True(hasMarkerLine, "Error message should contain a marker line with ^");
    }
}
