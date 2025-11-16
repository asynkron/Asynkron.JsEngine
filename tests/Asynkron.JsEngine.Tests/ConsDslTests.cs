using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Lisp;
using static Asynkron.JsEngine.Lisp.ConsDsl;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for the Cons DSL (ConsDsl.S helper method)
/// </summary>
public class ConsDslTests
{
    [Fact]
    public void S_CreatesConsFromArguments()
    {
        // Arrange & Act
        var result = S(Symbol.Intern("test"), 123, "hello");

        // Assert
        Assert.False(result.IsEmpty);
        Assert.Equal(Symbol.Intern("test"), result.Head);
        Assert.Equal(123, result.Rest.Head);
        Assert.Equal("hello", result.Rest.Rest.Head);
    }

    [Fact]
    public void S_WithNoArguments_CreatesEmptyCons()
    {
        // Arrange & Act
        var result = S();

        // Assert
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void S_WithSymbol_CreatesConsProperly()
    {
        // Arrange
        var symbol = Symbol.Intern("foo");

        // Act
        var result = S(symbol, 42);

        // Assert
        Assert.Equal(symbol, result.Head);
        Assert.Equal(42, result.Rest.Head);
    }

    [Fact]
    public void S_EquivalentToConsFrom()
    {
        // Arrange
        var args = new object[] { Symbol.Intern("test"), 123, "hello" };

        // Act
        var resultS = S(Symbol.Intern("test"), 123, "hello");
        var resultFrom = Cons.From(args);

        // Assert - both should create identical structures
        Assert.Equal(resultFrom.Head, resultS.Head);
        Assert.Equal(resultFrom.Rest.Head, resultS.Rest.Head);
        Assert.Equal(resultFrom.Rest.Rest.Head, resultS.Rest.Rest.Head);
    }

    [Fact]
    public void ConsFrom_CreatesConsFromArray()
    {
        // Arrange & Act
        var result = Cons.From(Symbol.Intern("add"), 1, 2);

        // Assert
        Assert.False(result.IsEmpty);
        Assert.Equal(Symbol.Intern("add"), result.Head);
        Assert.Equal(1, result.Rest.Head);
        Assert.Equal(2, result.Rest.Rest.Head);
    }

    [Fact]
    public void S_WithNestedCons_CreatesProperly()
    {
        // Arrange & Act - Create (outer (inner 1 2) 3)
        var inner = S(Symbol.Intern("inner"), 1, 2);
        var result = S(Symbol.Intern("outer"), inner, 3);

        // Assert
        Assert.Equal(Symbol.Intern("outer"), result.Head);
        var innerCons = result.Rest.Head as Cons;
        Assert.NotNull(innerCons);
        Assert.Equal(Symbol.Intern("inner"), innerCons!.Head);
        Assert.Equal(1, innerCons.Rest.Head);
        Assert.Equal(2, innerCons.Rest.Rest.Head);
        Assert.Equal(3, result.Rest.Rest.Head);
    }
}