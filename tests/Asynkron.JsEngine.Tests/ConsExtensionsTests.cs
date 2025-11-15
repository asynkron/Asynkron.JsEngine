using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using static Asynkron.JsEngine.Ast.ConsDsl;

namespace Asynkron.JsEngine.Tests;

public class ConsExtensionsTests
{
    [Fact]
    public void TryMatch_ReturnsFalse_ForWrongTag()
    {
        var cons = S(JsSymbols.Block, 1, 2);

        var matched = cons.TryMatch(JsSymbols.If, out var args);

        Assert.False(matched);
        Assert.True(args.IsEmpty);
    }

    [Fact]
    public void TryGetArguments_EnforcesExactArity()
    {
        var args = Cons.From(1, 2);
        Assert.True(args.TryGetArguments(out var first, out var second));
        Assert.Equal(1, first);
        Assert.Equal(2, second);

        var withExtra = Cons.From(1, 2, 3);
        Assert.False(withExtra.TryGetArguments(out _, out _));
    }

    [Fact]
    public void TryAsIfStatement_AllowsOptionalElse()
    {
        var withoutElse = S(JsSymbols.If, "cond", "then");
        Assert.True(withoutElse.TryAsIfStatement(out var condition, out var thenBranch, out var elseBranch));
        Assert.Equal("cond", condition);
        Assert.Equal("then", thenBranch);
        Assert.Null(elseBranch);

        var withElse = S(JsSymbols.If, "cond", "then", "else");
        Assert.True(withElse.TryAsIfStatement(out var elseCondition, out var elseThen, out var elseBody));
        Assert.Equal("cond", elseCondition);
        Assert.Equal("then", elseThen);
        Assert.Equal("else", elseBody);
    }

    [Fact]
    public void TryAsIfStatement_RejectsMalformedShape()
    {
        var malformed = S(JsSymbols.If, "onlyCondition");
        Assert.False(malformed.TryAsIfStatement(out _, out _, out _));

        var tooMany = S(JsSymbols.If, "cond", "then", "else", "oops");
        Assert.False(tooMany.TryAsIfStatement(out _, out _, out _));
    }

    [Fact]
    public void TryAsWhileStatement_RejectsExtraArguments()
    {
        var valid = S(JsSymbols.While, "cond", "body");
        Assert.True(valid.TryAsWhileStatement(out var condition, out var body));
        Assert.Equal("cond", condition);
        Assert.Equal("body", body);

        var invalid = S(JsSymbols.While, "cond", "body", "extra");
        Assert.False(invalid.TryAsWhileStatement(out _, out _));
    }

    [Fact]
    public void TryAsForStatement_RejectsMissingParts()
    {
        var valid = S(JsSymbols.For, "init", "cond", "inc", "body");
        Assert.True(valid.TryAsForStatement(out var init, out var cond, out var inc, out var body));
        Assert.Equal("init", init);
        Assert.Equal("cond", cond);
        Assert.Equal("inc", inc);
        Assert.Equal("body", body);

        var invalid = S(JsSymbols.For, "init", "cond");
        Assert.False(invalid.TryAsForStatement(out _, out _, out _, out _));
    }

    [Fact]
    public void TryAsLabelStatement_EnsuresSymbol()
    {
        var labelName = Symbol.Intern("loop");
        var cons = S(JsSymbols.Label, labelName, "body");

        Assert.True(cons.TryAsLabelStatement(out var parsedLabel, out var statement));
        Assert.Same(labelName, parsedLabel);
        Assert.Equal("body", statement);

        var missingSymbol = S(JsSymbols.Label, "notSymbol", "body");
        Assert.False(missingSymbol.TryAsLabelStatement(out _, out _));
    }
}
