namespace Asynkron.JsEngine.Parser;

/// <summary>
/// Distinguishes different brace contexts for regex vs division disambiguation.
/// </summary>
internal enum BraceKind
{
    /// <summary>Object literal - division follows</summary>
    ObjectLiteral,

    /// <summary>Function/method/class body - division follows (produces a value)</summary>
    FunctionBody,

    /// <summary>Statement block (if/for/while/etc.) - regex follows (no value)</summary>
    StatementBlock
}
