using Asynkron.JsEngine.Execution;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(WhileStatement statement)
    {
        private object? EvaluateWhile(JsEnvironment environment, EvaluationContext context,
            Symbol? loopLabel)
        {
            var isStrict = IsStrictBlock(statement.Body);
            if (!LoopNormalizer.TryNormalize(statement, isStrict, out var plan, out _))
            {
                throw new NotSupportedException("Failed to normalize while loop.");
            }

            return EvaluateLoopPlan(plan, environment, context, loopLabel);
        }
    }
}
