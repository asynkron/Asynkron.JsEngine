#region

using static Asynkron.JsEngine.StdLib.RegExpHelper;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    ///     Evaluates a regex literal by creating a RegExp object using the RealmState.
    /// </summary>
    private static JsValue EvaluateRegexLiteral(this RegexLiteralExpression regex, EvaluationContext context)
    {
        return new JsValue(CreateRegExpLiteral(regex.Pattern, regex.Flags, context.RealmState));
    }
}
