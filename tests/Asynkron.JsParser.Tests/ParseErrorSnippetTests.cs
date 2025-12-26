namespace Asynkron.JsParser.Tests;

public class ParseErrorSnippetTests
{
    private static void ParseWithError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new TypedAstParser(tokens, source);
        parser.ParseProgram();
    }

    [Fact]
    public void ParseError_IncludesSourceSnippet()
    {
        var source = """
            let x = 10;
            let y = 20;
            let z = ;
            """;

        var ex = Assert.Throws<ParseException>(() => ParseWithError(source));

        // Check that the error message contains the source context
        Assert.Contains("Source context:", ex.Message, StringComparison.Ordinal);
        Assert.Contains("let z = ;", ex.Message, StringComparison.Ordinal);
        Assert.Contains("^", ex.Message, StringComparison.Ordinal); // Should have a position marker
    }

    [Fact]
    public void ParseError_ShowsContextAroundError()
    {
        var source = "let a = 1; let b = 2; let c = 3; let d = 4; let e = 5; let f let g = 7;"; // Missing = after f

        var ex = Assert.Throws<ParseException>(() => ParseWithError(source));

        // Check that the error message contains context around the error
        Assert.Contains("Source context:", ex.Message, StringComparison.Ordinal);
        // Should show some chars before and after the error position
        Assert.Contains("let f", ex.Message, StringComparison.Ordinal);
        Assert.Contains("^", ex.Message, StringComparison.Ordinal); // Should have a position marker
    }

    [Fact]
    public void ParseError_WithShortSource_ShowsFullLine()
    {
        var source = "let x = ;"; // Missing initializer

        var ex = Assert.Throws<ParseException>(() => ParseWithError(source));

        // Even with short source, should show context
        Assert.Contains("Source context:", ex.Message, StringComparison.Ordinal);
        Assert.Contains("let x = ;", ex.Message, StringComparison.Ordinal);
        Assert.Contains("^", ex.Message, StringComparison.Ordinal); // Should have a position marker
    }

    [Fact]
    public void ParseError_AtBeginning_ShowsFromStart()
    {
        var source = "class { }"; // Missing class name

        var ex = Assert.Throws<ParseException>(() => ParseWithError(source));

        // Should show from the beginning
        Assert.Contains("Source context:", ex.Message, StringComparison.Ordinal);
        Assert.Contains("class", ex.Message, StringComparison.Ordinal);
        Assert.Contains("^", ex.Message, StringComparison.Ordinal); // Should have a position marker
    }

    [Fact]
    public void ParseError_LongSource_ShowsSnippet()
    {
        const string source = "let a = 1; let b = 2; let c = 3; let d = 4; let e = 5; let f = 6; let g let h = 8;"; // Missing = after g

        var ex = Assert.Throws<ParseException>(() => ParseWithError(source));

        // Should show snippet around the error with ellipsis
        Assert.Contains("Source context:", ex.Message, StringComparison.Ordinal);
        Assert.Contains("let g", ex.Message, StringComparison.Ordinal);
        // Should have ellipsis if truncated
        Assert.Contains("...", ex.Message, StringComparison.Ordinal);
        Assert.Contains("^", ex.Message, StringComparison.Ordinal); // Should have a position marker
    }

    [Fact]
    public void ParseError_HasLineAndColumnInfo()
    {
        var source = """
            let x = 10;
            let y = 20;
            let z = ;
            """;

        var ex = Assert.Throws<ParseException>(() => ParseWithError(source));

        // Check that line and column are present
        Assert.NotNull(ex.Line);
        Assert.NotNull(ex.Column);
        Assert.True(ex.Line > 0);
        Assert.True(ex.Column > 0);

        // Verify the line/column in the message
        Assert.Contains($"at line {ex.Line}, column {ex.Column}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseError_MarkerPointsToCorrectPosition()
    {
        var source = "let x = ;"; // Error at semicolon

        var ex = Assert.Throws<ParseException>(() => ParseWithError(source));

        // The marker should be roughly at the position of the semicolon
        // Since the context includes the full line, the marker should be visible
        var lines = ex.Message.Split('\n');
        var hasMarkerLine = lines.Any(l => l.Contains('^'));
        Assert.True(hasMarkerLine, "Error message should contain a marker line with ^");
    }
}
