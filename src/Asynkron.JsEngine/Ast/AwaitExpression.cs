using System.Globalization;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an await expression.
/// </summary>
public sealed partial record AwaitExpression(SourceReference? Source, ExpressionNode Expression)
    : ExpressionNode(Source), IAstCacheable<Symbol>;

public sealed partial record AwaitExpression
{
    private static int NextAwaitStateId;
    private Symbol? _cachedAwaitStateKey;

    Symbol IAstCacheable<Symbol>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(ref _cachedAwaitStateKey, this, static _ => BuildAwaitStateKey());
    }

    private static Symbol BuildAwaitStateKey()
    {
        var id = Interlocked.Increment(ref NextAwaitStateId);
        var key = string.Format(
            CultureInfo.InvariantCulture,
            "__await_state_{0}",
            id);
        return Symbol.Intern(key);
    }
}
