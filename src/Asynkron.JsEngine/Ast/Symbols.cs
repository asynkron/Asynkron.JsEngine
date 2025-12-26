namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Shared well-known ECMAScript symbols.
/// </summary>
public static class Symbols
{
    public static readonly TypedAstSymbol Iterator = TypedAstSymbol.For("Symbol.iterator");
    public static readonly TypedAstSymbol AsyncIterator = TypedAstSymbol.For("Symbol.asyncIterator");
    public static readonly TypedAstSymbol HasInstance = TypedAstSymbol.For("Symbol.hasInstance");
    public static readonly TypedAstSymbol ToPrimitive = TypedAstSymbol.For("Symbol.toPrimitive");
    public static readonly TypedAstSymbol ToStringTag = TypedAstSymbol.For("Symbol.toStringTag");
    public static readonly TypedAstSymbol Species = TypedAstSymbol.For("Symbol.species");
    public static readonly TypedAstSymbol Match = TypedAstSymbol.For("Symbol.match");
    public static readonly TypedAstSymbol MatchAll = TypedAstSymbol.For("Symbol.matchAll");
    public static readonly TypedAstSymbol Replace = TypedAstSymbol.For("Symbol.replace");
    public static readonly TypedAstSymbol ReplaceAll = TypedAstSymbol.For("Symbol.replaceAll");
    public static readonly TypedAstSymbol Search = TypedAstSymbol.For("Symbol.search");
    public static readonly TypedAstSymbol Split = TypedAstSymbol.For("Symbol.split");
    public static readonly TypedAstSymbol IsConcatSpreadable = TypedAstSymbol.For("Symbol.isConcatSpreadable");
    public static readonly TypedAstSymbol Unscopables = TypedAstSymbol.For("Symbol.unscopables");
    public static readonly TypedAstSymbol Dispose = TypedAstSymbol.For("Symbol.dispose");
    public static readonly TypedAstSymbol AsyncDispose = TypedAstSymbol.For("Symbol.asyncDispose");
}
