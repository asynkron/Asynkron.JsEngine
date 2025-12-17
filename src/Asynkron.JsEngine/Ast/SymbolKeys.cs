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

    public static string GetIterator(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.iterator") ?? Iterator;

    public static string GetAsyncIterator(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.asyncIterator") ?? AsyncIterator;

    public static string GetHasInstance(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.hasInstance") ?? HasInstance;

    public static string GetToPrimitive(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.toPrimitive") ?? ToPrimitive;

    public static string GetToStringTag(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.toStringTag") ?? ToStringTag;

    public static string GetSpecies(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.species") ?? Species;

    public static string GetMatch(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.match") ?? Match;

    public static string GetMatchAll(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.matchAll") ?? MatchAll;

    public static string GetReplace(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.replace") ?? Replace;

    public static string GetReplaceAll(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.replaceAll") ?? ReplaceAll;

    public static string GetSearch(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.search") ?? Search;

    public static string GetSplit(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.split") ?? Split;

    public static string GetIsConcatSpreadable(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.isConcatSpreadable") ?? IsConcatSpreadable;

    public static string GetUnscopables(Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey("Symbol.unscopables") ?? Unscopables;

    public static string GetKey(TypedAstSymbol symbol, Runtime.RealmState? realm) =>
        realm?.GetSymbolPropertyKey(symbol.Description ?? symbol.ToString()) ?? TypedAstSymbol.PropertyKey(symbol);
}
