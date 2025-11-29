using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(LiteralExpression literal)
    {
        private object? EvaluateLiteral(EvaluationContext context)
        {
            return literal.Value switch
            {
                RegexLiteralValue regex => StandardLibrary.CreateRegExpLiteral(regex.Pattern, regex.Flags,
                    context.RealmState),
                _ => literal.Value
            };
        }
    }

}
