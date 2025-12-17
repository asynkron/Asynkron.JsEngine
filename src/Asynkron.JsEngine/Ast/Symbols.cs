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
}

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
