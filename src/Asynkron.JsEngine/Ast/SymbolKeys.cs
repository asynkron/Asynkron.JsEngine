namespace Asynkron.JsEngine.Ast;

public static class SymbolKeys
{
    public static readonly string Iterator = TypedAstSymbol.PropertyKey(Symbols.Iterator);
    public static readonly string AsyncIterator = TypedAstSymbol.PropertyKey(Symbols.AsyncIterator);
    public static readonly string HasInstance = TypedAstSymbol.PropertyKey(Symbols.HasInstance);
    public static readonly string ToPrimitive = TypedAstSymbol.PropertyKey(Symbols.ToPrimitive);
    public static readonly string ToStringTag = TypedAstSymbol.PropertyKey(Symbols.ToStringTag);
    public static readonly string Species = TypedAstSymbol.PropertyKey(Symbols.Species);
    public static readonly string Match = TypedAstSymbol.PropertyKey(Symbols.Match);
    public static readonly string MatchAll = TypedAstSymbol.PropertyKey(Symbols.MatchAll);
    public static readonly string Replace = TypedAstSymbol.PropertyKey(Symbols.Replace);
    public static readonly string ReplaceAll = TypedAstSymbol.PropertyKey(Symbols.ReplaceAll);
    public static readonly string Search = TypedAstSymbol.PropertyKey(Symbols.Search);
    public static readonly string Split = TypedAstSymbol.PropertyKey(Symbols.Split);
    public static readonly string IsConcatSpreadable = TypedAstSymbol.PropertyKey(Symbols.IsConcatSpreadable);
    public static readonly string Unscopables = TypedAstSymbol.PropertyKey(Symbols.Unscopables);
}
