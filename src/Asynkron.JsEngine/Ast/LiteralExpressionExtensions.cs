using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(LiteralExpression literal)
    {
        /// <summary>
        ///     Evaluates a literal expression by returning the pre-computed JsValue.
        /// </summary>
        private JsValue EvaluateLiteral(EvaluationContext context) => literal.Value;
    }
}
