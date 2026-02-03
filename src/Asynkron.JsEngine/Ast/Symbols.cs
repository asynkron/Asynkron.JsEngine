namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Shared well-known ECMAScript symbols.
/// </summary>
public static class Symbols
{
    public static readonly JsSymbol Iterator = JsSymbol.Create("Symbol.iterator");
    public static readonly JsSymbol AsyncIterator = JsSymbol.Create("Symbol.asyncIterator");
    public static readonly JsSymbol HasInstance = JsSymbol.Create("Symbol.hasInstance");
    public static readonly JsSymbol ToPrimitive = JsSymbol.Create("Symbol.toPrimitive");
    public static readonly JsSymbol ToStringTag = JsSymbol.Create("Symbol.toStringTag");
    public static readonly JsSymbol Species = JsSymbol.Create("Symbol.species");
    public static readonly JsSymbol Match = JsSymbol.Create("Symbol.match");
    public static readonly JsSymbol MatchAll = JsSymbol.Create("Symbol.matchAll");
    public static readonly JsSymbol Replace = JsSymbol.Create("Symbol.replace");
    public static readonly JsSymbol ReplaceAll = JsSymbol.Create("Symbol.replaceAll");
    public static readonly JsSymbol Search = JsSymbol.Create("Symbol.search");
    public static readonly JsSymbol Split = JsSymbol.Create("Symbol.split");
    public static readonly JsSymbol IsConcatSpreadable = JsSymbol.Create("Symbol.isConcatSpreadable");
    public static readonly JsSymbol Unscopables = JsSymbol.Create("Symbol.unscopables");
    public static readonly JsSymbol Dispose = JsSymbol.Create("Symbol.dispose");
    public static readonly JsSymbol AsyncDispose = JsSymbol.Create("Symbol.asyncDispose");
}
