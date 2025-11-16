using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Lisp;
using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for source reference tracking on s-expressions.
/// </summary>
public class SourceReferenceTests
{
    [Fact(Timeout = 2000)]
    public async Task SourceReference_ForLoop_CapturesSourceRange()
    {
        var source = """

                     for (var x = 0; x < 10; x++) {
                         console.log(x);
                     }
                     """;

        await using var engine = new JsEngine();
        var parsed = JsEngine.ParseWithoutTransformation(source);

        // Navigate to the for loop statement
        // parsed is (program for-statement)
        var forStatement = parsed.Rest.Head as Cons;

        Assert.NotNull(forStatement);
        Assert.NotNull(forStatement!.SourceReference);

        // Verify the source text captured
        var capturedText = forStatement.SourceReference!.GetText();
        Assert.Contains("for", capturedText);
        Assert.Contains("console.log", capturedText);
    }

    [Fact(Timeout = 2000)]
    public async Task SourceReference_MultipleStatements_EachHasOwnReference()
    {
        var source = """

                     for (var i = 0; i < 5; i++) { }
                     for (var j = 0; j < 3; j++) { }
                     """;

        await using var engine = new JsEngine();
        var parsed = JsEngine.ParseWithoutTransformation(source);

        // parsed is (program statement1 statement2)
        var firstStatement = parsed.Rest.Head as Cons;
        var secondStatement = parsed.Rest.Rest.Head as Cons;

        // Both for statements should have source references
        Assert.NotNull(firstStatement);
        Assert.NotNull(secondStatement);

        // They should have source references (we added source tracking to ParseForStatement)
        Assert.NotNull(firstStatement!.SourceReference);
        Assert.NotNull(secondStatement!.SourceReference);

        var firstText = firstStatement.SourceReference!.GetText();
        var secondText = secondStatement.SourceReference!.GetText();

        Assert.Contains("i < 5", firstText);
        Assert.Contains("j < 3", secondText);
    }

    [Fact(Timeout = 2000)]
    public async Task SourceReference_GetText_ReturnsCorrectSourceText()
    {
        var source = @"for (let i = 0; i < 5; i++) { }";

        await using var engine = new JsEngine();
        var parsed = JsEngine.ParseWithoutTransformation(source);

        var forStatement = parsed.Rest.Head as Cons;

        Assert.NotNull(forStatement);
        Assert.NotNull(forStatement!.SourceReference);

        var text = forStatement.SourceReference!.GetText();

        // The captured text should contain the entire for loop
        Assert.Contains("for", text);
        Assert.Contains("let i = 0", text);
        Assert.Contains("i < 5", text);
        Assert.Contains("i++", text);
    }

    [Fact(Timeout = 2000)]
    public async Task SourceReference_LineAndColumn_TrackCorrectly()
    {
        var source = """
                     let x = 1;
                     for (let i = 0; i < 5; i++) {
                         x++;
                     }
                     """;

        await using var engine = new JsEngine();
        var parsed = JsEngine.ParseWithoutTransformation(source);

        // The for loop is on line 2
        var forStatement = parsed.Rest.Rest.Head as Cons;

        Assert.NotNull(forStatement);
        var sourceRef = forStatement!.SourceReference;
        Assert.NotNull(sourceRef);

        // Verify line numbers (line 2 is where 'for' starts)
        // Note: Line numbers are 1-based
        Assert.True(sourceRef!.StartLine >= 2);
    }
}
