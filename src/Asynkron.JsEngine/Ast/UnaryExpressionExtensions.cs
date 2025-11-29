using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(UnaryExpression expression)
    {
        private object? EvaluateUnary(JsEnvironment environment,
            EvaluationContext context)
        {
            switch (expression.Operator)
            {
                case "++" or "--":
                {
                    var reference = AssignmentReferenceResolver.Resolve(
                        expression.Operand,
                        environment,
                        context,
                        EvaluateExpression);
                    var currentValue = reference.GetValue();
                    var updatedValue = expression.Operator == "++"
                        ? IncrementValue(currentValue, context)
                        : DecrementValue(currentValue, context);
                    reference.SetValue(updatedValue);
                    return expression.IsPrefix ? updatedValue : currentValue;
                }
                case "delete":
                    return EvaluateDelete(expression.Operand, environment, context);
                case "typeof" when expression.Operand is IdentifierExpression identifier &&
                                   !environment.TryGet(identifier.Name, out var value):
                    return "undefined";
                case "typeof":
                {
                    var operandValue = EvaluateExpression(expression.Operand, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return Symbol.Undefined;
                    }

                    return GetTypeofString(operandValue);
                }
            }

            var operand = EvaluateExpression(expression.Operand, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            return expression.Operator switch
            {
                "!" => !IsTruthy(operand),
                "+" => operand is JsBigInt
                    ? throw StandardLibrary.ThrowTypeError("Cannot convert a BigInt value to a number", context)
                    : JsOps.ToNumber(operand, context),
                "-" => operand is JsBigInt bigInt ? -bigInt : -JsOps.ToNumber(operand, context),
                "~" => BitwiseNot(operand, context),
                "void" => Symbol.Undefined,
                _ => throw new NotSupportedException($"Operator '{expression.Operator}' is not supported yet.")
            };
        }
    }

}
