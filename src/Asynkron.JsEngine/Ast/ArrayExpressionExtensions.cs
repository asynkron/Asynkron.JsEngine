using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ArrayExpression expression)
    {
        private object? EvaluateArray(JsEnvironment environment,
            EvaluationContext context)
        {
            var array = new JsArray(context.RealmState);
            foreach (var element in expression.Elements)
            {
                if (element.IsSpread)
                {
                    var spreadValue = EvaluateExpression(element.Expression!, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return Symbol.Undefined;
                    }

                    foreach (var item in EnumerateSpread(spreadValue, context))
                    {
                        array.Push(item);
                    }

                    continue;
                }

                if (element.Expression is null)
                {
                    array.PushHole();
                }
                else
                {
                    array.Push(EvaluateExpression(element.Expression, environment, context));
                }

                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }
            }

            StandardLibrary.AddArrayMethods(array, context.RealmState);
            return array;
        }
    }
}
