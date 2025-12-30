#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a regex literal. Kept separate because regex objects require RealmState at runtime.
///     Implements IAstCacheable to cache the normalized pattern and compiled .NET Regex.
/// </summary>
public sealed record RegexLiteralExpression(SourceReference? Source, string Pattern, string Flags)
    : ExpressionNode(Source), IAstCacheable<RegexCache>
{
    private RegexCache? _cachedRegex;

    RegexCache IAstCacheable<RegexCache>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(ref _cachedRegex, this, static self => new RegexCache(self.Pattern, self.Flags));
    }
}
