using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(WithStatement statement)
    {
        private object? EvaluateWith(JsEnvironment environment, EvaluationContext context)
        {
            var objValue = EvaluateExpression(statement.Object, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return objValue;
            }

            if (!TryConvertToWithBindingObject(objValue, context, out var withObject))
            {
                return Symbol.Undefined;
            }

            var withEnv = new JsEnvironment(environment, false, context.CurrentScope.IsStrict, statement.Source, "with",
                withObject);
            var completion = EvaluateStatement(statement.Body, withEnv, context);

            return ReferenceEquals(completion, EmptyCompletion)
                ? Symbol.Undefined
                : completion;
        }
    }
}
