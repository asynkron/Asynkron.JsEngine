using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(RegexLiteralExpression regex)
    {
        /// <summary>
        ///     Evaluates a regex literal by creating a RegExp object using the RealmState.
        /// </summary>
        private JsValue EvaluateRegexLiteral(EvaluationContext context)
        {
            return new JsValue(StandardLibrary.CreateRegExpLiteral(regex.Pattern, regex.Flags, context.RealmState));
        }
    }
}
