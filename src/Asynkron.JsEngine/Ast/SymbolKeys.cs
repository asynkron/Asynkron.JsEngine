namespace Asynkron.JsEngine.Ast;

public static class SymbolKeys
{
    public static readonly string Iterator = JsSymbol.PropertyKey(Symbols.Iterator);
    public static readonly string AsyncIterator = JsSymbol.PropertyKey(Symbols.AsyncIterator);
    public static readonly string HasInstance = JsSymbol.PropertyKey(Symbols.HasInstance);
    public static readonly string ToPrimitive = JsSymbol.PropertyKey(Symbols.ToPrimitive);
    public static readonly string ToStringTag = JsSymbol.PropertyKey(Symbols.ToStringTag);
    public static readonly string Species = JsSymbol.PropertyKey(Symbols.Species);
    public static readonly string Match = JsSymbol.PropertyKey(Symbols.Match);
    public static readonly string MatchAll = JsSymbol.PropertyKey(Symbols.MatchAll);
    public static readonly string Replace = JsSymbol.PropertyKey(Symbols.Replace);
    public static readonly string ReplaceAll = JsSymbol.PropertyKey(Symbols.ReplaceAll);
    public static readonly string Search = JsSymbol.PropertyKey(Symbols.Search);
    public static readonly string Split = JsSymbol.PropertyKey(Symbols.Split);
    public static readonly string IsConcatSpreadable = JsSymbol.PropertyKey(Symbols.IsConcatSpreadable);
    public static readonly string Unscopables = JsSymbol.PropertyKey(Symbols.Unscopables);
    public static readonly string Dispose = JsSymbol.PropertyKey(Symbols.Dispose);
    public static readonly string AsyncDispose = JsSymbol.PropertyKey(Symbols.AsyncDispose);
}
