using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Lisp;

namespace Asynkron.JsEngine.Parser;

/// <summary>
/// Transitional parser that keeps using the existing Cons-based parser internally
/// but immediately converts the resulting S-expression into the typed AST.
/// Future work will replace the Cons dependency entirely, but this class lets
/// the runtime consume typed nodes without keeping the Cons tree alive.
/// </summary>
public sealed class TypedAstParser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly string _source;
    private readonly SExpressionAstBuilder _astBuilder;

    public TypedAstParser(IReadOnlyList<Token> tokens, string source, SExpressionAstBuilder? astBuilder = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _source = source ?? string.Empty;
        _astBuilder = astBuilder ?? new SExpressionAstBuilder();
    }

    /// <summary>
    /// Parses the provided tokens into the typed AST.
    /// </summary>
    public ProgramNode ParseProgram()
    {
        var consProgram = ParseConsProgram();
        return _astBuilder.BuildProgram(consProgram);
    }

    private Cons ParseConsProgram()
    {
        var parser = new Parser(_tokens, _source);
        return parser.ParseProgram();
    }
}
