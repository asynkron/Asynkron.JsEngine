#region

using static Asynkron.JsEngine.StdLib.RegExpHelper;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    ///     Evaluates a regex literal by creating a RegExp object using the RealmState.
    /// </summary>
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateRegexLiteral(this RegexLiteralExpression regex, EvaluationContext context)
    {
        return new JsValue(CreateRegExpLiteral(regex.Pattern, regex.Flags, context.RealmState));
    }
}
