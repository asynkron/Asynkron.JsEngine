using Asynkron.JsEngine.Parser;
using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for JavaScript labeled statements.
/// Labeled statements allow naming a statement so it can be referenced by break/continue.
/// </summary>
public class LabeledStatementTests
{
    [Fact(Timeout = 2000)]
    public Task ParseLabeledForLoop()
    {
        var source = "depthLoop: for (var _i = 0; _i < 5; _i++) { break; }";
        var program = JsEngine.ParseWithoutTransformation(source);

        Assert.Same(JsSymbols.Program, program.Head);
        var labeledStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Label, labeledStatement.Head);
        Assert.Equal(Symbol.Intern("depthLoop"), labeledStatement.Rest.Head);
        
        var forLoop = Assert.IsType<Cons>(labeledStatement.Rest.Rest.Head);
        Assert.Same(JsSymbols.For, forLoop.Head);
        
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task ParseLabeledWhileLoop()
    {
        var source = "myLabel: while (false) { console.log('test'); }";
        var program = JsEngine.ParseWithoutTransformation(source);

        Assert.Same(JsSymbols.Program, program.Head);
        var labeledStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Label, labeledStatement.Head);
        Assert.Equal(Symbol.Intern("myLabel"), labeledStatement.Rest.Head);
        
        var whileLoop = Assert.IsType<Cons>(labeledStatement.Rest.Rest.Head);
        Assert.Same(JsSymbols.While, whileLoop.Head);
        
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task ParseLabeledBlock()
    {
        var source = "blockLabel: { console.log('test'); }";
        var program = JsEngine.ParseWithoutTransformation(source);

        Assert.Same(JsSymbols.Program, program.Head);
        var labeledStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Label, labeledStatement.Head);
        Assert.Equal(Symbol.Intern("blockLabel"), labeledStatement.Rest.Head);
        
        var block = Assert.IsType<Cons>(labeledStatement.Rest.Rest.Head);
        Assert.Same(JsSymbols.Block, block.Head);
        
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task ParseLabeledExpressionStatement()
    {
        var source = "myLabel: console.log('test');";
        var program = JsEngine.ParseWithoutTransformation(source);

        Assert.Same(JsSymbols.Program, program.Head);
        var labeledStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Label, labeledStatement.Head);
        Assert.Equal(Symbol.Intern("myLabel"), labeledStatement.Rest.Head);
        
        var exprStmt = Assert.IsType<Cons>(labeledStatement.Rest.Rest.Head);
        Assert.Same(JsSymbols.ExpressionStatement, exprStmt.Head);
        
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task ParseNestedLabels()
    {
        var source = "outer: inner: console.log('test');";
        var program = JsEngine.ParseWithoutTransformation(source);

        Assert.Same(JsSymbols.Program, program.Head);
        var outerLabel = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Label, outerLabel.Head);
        Assert.Equal(Symbol.Intern("outer"), outerLabel.Rest.Head);
        
        var innerLabel = Assert.IsType<Cons>(outerLabel.Rest.Rest.Head);
        Assert.Same(JsSymbols.Label, innerLabel.Head);
        Assert.Equal(Symbol.Intern("inner"), innerLabel.Rest.Head);
        
        var exprStmt = Assert.IsType<Cons>(innerLabel.Rest.Rest.Head);
        Assert.Same(JsSymbols.ExpressionStatement, exprStmt.Head);
        
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task ParseBreakWithLabel()
    {
        var source = "outer: for (var i = 0; i < 5; i++) { break outer; }";
        var program = JsEngine.ParseWithoutTransformation(source);

        Assert.Same(JsSymbols.Program, program.Head);
        var labeledStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Label, labeledStatement.Head);
        
        var forLoop = Assert.IsType<Cons>(labeledStatement.Rest.Rest.Head);
        var block = Assert.IsType<Cons>(forLoop.Rest.Rest.Rest.Rest.Head);
        var breakStmt = Assert.IsType<Cons>(block.Rest.Head);
        Assert.Same(JsSymbols.Break, breakStmt.Head);
        Assert.Equal(Symbol.Intern("outer"), breakStmt.Rest.Head);
        
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task ParseContinueWithLabel()
    {
        var source = "outer: for (var i = 0; i < 5; i++) { continue outer; }";
        var program = JsEngine.ParseWithoutTransformation(source);

        Assert.Same(JsSymbols.Program, program.Head);
        var labeledStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Label, labeledStatement.Head);
        
        var forLoop = Assert.IsType<Cons>(labeledStatement.Rest.Rest.Head);
        var block = Assert.IsType<Cons>(forLoop.Rest.Rest.Rest.Rest.Head);
        var continueStmt = Assert.IsType<Cons>(block.Rest.Head);
        Assert.Same(JsSymbols.Continue, continueStmt.Head);
        Assert.Equal(Symbol.Intern("outer"), continueStmt.Rest.Head);
        
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task ParseBreakWithoutLabel()
    {
        var source = "for (var i = 0; i < 5; i++) { break; }";
        var program = JsEngine.ParseWithoutTransformation(source);

        Assert.Same(JsSymbols.Program, program.Head);
        var forLoop = Assert.IsType<Cons>(program.Rest.Head);
        var block = Assert.IsType<Cons>(forLoop.Rest.Rest.Rest.Rest.Head);
        var breakStmt = Assert.IsType<Cons>(block.Rest.Head);
        Assert.Same(JsSymbols.Break, breakStmt.Head);
        // No label, so Rest should be empty
        Assert.True(breakStmt.Rest is null || ReferenceEquals(breakStmt.Rest, Cons.Empty));
        
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task RejectLabelBeforeLetDeclaration()
    {
        var source = "myLabel: let x = 5;";
        
        var exception = Assert.Throws<ParseException>(() =>
        {
            JsEngine.ParseWithoutTransformation(source);
        });
        
        Assert.Contains("Let", exception.Message);
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task RejectLabelBeforeConstDeclaration()
    {
        var source = "myLabel: const x = 5;";
        
        var exception = Assert.Throws<ParseException>(() =>
        {
            JsEngine.ParseWithoutTransformation(source);
        });
        
        Assert.Contains("Const", exception.Message);
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task RejectLabelBeforeVarDeclaration()
    {
        var source = "myLabel: var x = 5;";
        
        var exception = Assert.Throws<ParseException>(() =>
        {
            JsEngine.ParseWithoutTransformation(source);
        });
        
        Assert.Contains("Var", exception.Message);
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task RejectLabelBeforeClassDeclaration()
    {
        var source = "myLabel: class Foo {}";
        
        var exception = Assert.Throws<ParseException>(() =>
        {
            JsEngine.ParseWithoutTransformation(source);
        });
        
        Assert.Contains("Class", exception.Message);
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task ParseLabeledIfStatement()
    {
        var source = "myLabel: if (true) console.log('test');";
        var program = JsEngine.ParseWithoutTransformation(source);

        Assert.Same(JsSymbols.Program, program.Head);
        var labeledStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Label, labeledStatement.Head);
        Assert.Equal(Symbol.Intern("myLabel"), labeledStatement.Rest.Head);
        
        var ifStmt = Assert.IsType<Cons>(labeledStatement.Rest.Rest.Head);
        Assert.Same(JsSymbols.If, ifStmt.Head);
        
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task ParseComplexNestedLabelsWithBreak()
    {
        var source = @"
            outer: while (true) {
                inner: while (true) {
                    break outer;
                }
            }
        ";
        var program = JsEngine.ParseWithoutTransformation(source);

        Assert.Same(JsSymbols.Program, program.Head);
        var outerLabel = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Label, outerLabel.Head);
        Assert.Equal(Symbol.Intern("outer"), outerLabel.Rest.Head);
        
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task ParseLabelWithFunctionExpression()
    {
        // Note: When a function appears after a label, it's parsed as a function expression,
        // not a function declaration. This is correct JavaScript behavior.
        var source = "myLabel: function foo() {}";
        var program = JsEngine.ParseWithoutTransformation(source);

        Assert.Same(JsSymbols.Program, program.Head);
        var labeledStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Label, labeledStatement.Head);
        
        var exprStmt = Assert.IsType<Cons>(labeledStatement.Rest.Rest.Head);
        Assert.Same(JsSymbols.ExpressionStatement, exprStmt.Head);
        
        // The function should be parsed as a lambda (function expression), not a function declaration
        var lambda = Assert.IsType<Cons>(exprStmt.Rest.Head);
        Assert.Same(JsSymbols.Lambda, lambda.Head);
        
        return Task.CompletedTask;
    }
}
