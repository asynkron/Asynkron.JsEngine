#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static string GetTypeofStringValue(in JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "object",
            JsValueKind.Boolean => "boolean",
            JsValueKind.Number => "number",
            JsValueKind.BigInt => "bigint",
            JsValueKind.String => "string",
            JsValueKind.Symbol => "symbol",
            JsValueKind.Object => GetTypeofStringForObject(value.ObjectValue),
            _ => "undefined"
        };
    }

    private static string GetTypeofStringForObject(object? obj)
    {
        if (obj is IIsHtmlDda)
        {
            return "undefined";
        }

        if (obj is JsProxy proxy)
        {
            return proxy.IsCallableTarget() ? "function" : "object";
        }

        return obj is IJsCallable ? "function" : "object";
    }

    private static JsValue BitwiseNotValue(in JsValue operand, EvaluationContext context)
    {
        return BitwiseNotJsValue(in operand, context);
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateUnary(this UnaryExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression is
            {
                Operator: UnaryOperator.Delete,
                Operand: MemberExpression { IsOptional: true }
            })
        {
            return expression.Operand.EvaluateDelete(environment, context) ? JsValue.True : JsValue.False;
        }

        return EvaluateCachedExpressionProgram(
            expression,
            environment,
            context,
            "Dynamic unary expression");
    }
}
