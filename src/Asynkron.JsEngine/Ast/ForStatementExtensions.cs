using Asynkron.JsEngine.Execution;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(ForStatement statement)
    {
        private object? EvaluateFor(JsEnvironment environment, EvaluationContext context,
            Symbol? loopLabel)
        {
            var isStrict = IsStrictBlock(statement.Body);
            if (!LoopNormalizer.TryNormalize(statement, isStrict, out var plan, out _))
            {
                throw new NotSupportedException("Failed to normalize for loop.");
            }

            var loopEnvironment = new JsEnvironment(environment, creatingSource: statement.Source, description: "for-loop");
            return EvaluateLoopPlan(plan, loopEnvironment, context, loopLabel);
        }
    }

}
