using System.Collections.Generic;
using Asynkron.JsEngine.Lisp;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Represents the parsed program that the runtime executes. The primary data is
/// the typed AST, but callers can request the legacy Cons representation on
/// demand for tooling or fallback transformations. The legacy tree is rebuilt
/// lazily from the stored tokens so most execution paths never pay for it.
/// </summary>
public sealed class ParsedProgram
{
    private Cons? _sExpression;

    public ParsedProgram(ProgramNode typed, IReadOnlyList<Token>? tokens = null, string? source = null,
        Cons? sExpression = null)
    {
        Typed = typed ?? throw new ArgumentNullException(nameof(typed));
        Tokens = tokens;
        Source = source;
        _sExpression = sExpression;
    }

    public ProgramNode Typed { get; }
    internal IReadOnlyList<Token>? Tokens { get; }
    internal string? Source { get; }

    /// <summary>
    /// Returns the legacy Cons representation, parsing it lazily if needed.
    /// </summary>
    public Cons EnsureSExpression()
    {
        if (_sExpression is not null)
        {
            return _sExpression;
        }

        if (Tokens is null || Source is null)
        {
            throw new InvalidOperationException(
                "No legacy S-expression is available for this program. It may have been parsed outside the legacy pipeline.");
        }

        var parser = new Parser.Parser(Tokens, Source);
        _sExpression = parser.ParseProgram();
        return _sExpression;
    }

    internal ParsedProgram WithTyped(ProgramNode typed, Cons? explicitSExpression = null)
    {
        return new ParsedProgram(typed, Tokens, Source, explicitSExpression ?? _sExpression);
    }

    public static ParsedProgram FromSExpression(Cons sExpression, ProgramNode typed)
    {
        return new ParsedProgram(typed, null, null, sExpression);
    }
}
