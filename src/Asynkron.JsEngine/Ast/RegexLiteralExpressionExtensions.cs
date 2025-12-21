using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;
using static Asynkron.JsEngine.StdLib.RegExpHelper;

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
            return new JsValue(CreateRegExpLiteral(regex.Pattern, regex.Flags, context.RealmState));
        }
    }
}
