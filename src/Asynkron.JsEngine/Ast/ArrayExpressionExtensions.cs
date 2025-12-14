using Asynkron.JsEngine.JsTypes;

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
                    var spreadValueJs = EvaluateExpression(element.Expression!, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return Symbol.Undefined;
                    }

                    foreach (var item in EnumerateSpread(spreadValueJs.ToObject(), context))
                    {
                        array.Push(item);
                    }

                    if (context.ShouldStopEvaluation)
                    {
                        return Symbol.Undefined;
                    }

                    continue;
                }

                if (element.Expression is null)
                {
                    array.PushHole();
                }
                else
                {
                    array.Push(EvaluateExpression(element.Expression, environment, context).ToObject());
                }

                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }
            }

            return array;
        }
    }
}
