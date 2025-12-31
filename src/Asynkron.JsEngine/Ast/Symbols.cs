namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Shared well-known ECMAScript symbols.
/// </summary>
public static class Symbols
{
    public static readonly JsSymbol Iterator = JsSymbol.For("Symbol.iterator");
    public static readonly JsSymbol AsyncIterator = JsSymbol.For("Symbol.asyncIterator");
    public static readonly JsSymbol HasInstance = JsSymbol.For("Symbol.hasInstance");
    public static readonly JsSymbol ToPrimitive = JsSymbol.For("Symbol.toPrimitive");
    public static readonly JsSymbol ToStringTag = JsSymbol.For("Symbol.toStringTag");
    public static readonly JsSymbol Species = JsSymbol.For("Symbol.species");
    public static readonly JsSymbol Match = JsSymbol.For("Symbol.match");
    public static readonly JsSymbol MatchAll = JsSymbol.For("Symbol.matchAll");
    public static readonly JsSymbol Replace = JsSymbol.For("Symbol.replace");
    public static readonly JsSymbol ReplaceAll = JsSymbol.For("Symbol.replaceAll");
    public static readonly JsSymbol Search = JsSymbol.For("Symbol.search");
    public static readonly JsSymbol Split = JsSymbol.For("Symbol.split");
    public static readonly JsSymbol IsConcatSpreadable = JsSymbol.For("Symbol.isConcatSpreadable");
    public static readonly JsSymbol Unscopables = JsSymbol.For("Symbol.unscopables");
    public static readonly JsSymbol Dispose = JsSymbol.For("Symbol.dispose");
    public static readonly JsSymbol AsyncDispose = JsSymbol.For("Symbol.asyncDispose");
}
