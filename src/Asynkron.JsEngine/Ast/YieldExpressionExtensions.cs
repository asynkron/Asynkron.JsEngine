#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(YieldExpression expression)
    {
        private JsValue EvaluateYield(JsEnvironment environment,
            EvaluationContext context)
        {
            // All yield expressions should be lowered by GeneratorYieldLowerer and compiled to IR.
            // If we reach here, it means the lowerer missed a pattern.
            throw new InvalidOperationException(
                "Yield expression encountered during AST evaluation. " +
                "This should have been lowered to IR by GeneratorYieldLowerer. " +
                $"Source: {expression.Source?.StartPosition}-{expression.Source?.EndPosition}");
        }
    }
}
