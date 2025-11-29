using Asynkron.JsEngine.Execution;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(DoWhileStatement statement)
    {
        private object? EvaluateDoWhile(JsEnvironment environment,
            EvaluationContext context,
            Symbol? loopLabel)
        {
            var isStrict = IsStrictBlock(statement.Body);
            if (!LoopNormalizer.TryNormalize(statement, isStrict, out var plan, out _))
            {
                throw new NotSupportedException("Failed to normalize do/while loop.");
            }

            return EvaluateLoopPlan(plan, environment, context, loopLabel);
        }
    }
}
