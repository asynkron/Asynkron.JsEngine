using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(BinaryExpression expression)
    {
        private object? EvaluateBinary(JsEnvironment environment,
            EvaluationContext context)
        {
            // ES2022: Handle private identifier in 'in' operator (#field in obj)
            if (expression.Operator == "in" && expression.Left is PrivateIdentifierExpression privateId)
            {
                var rightTarget = EvaluateExpression(expression.Right, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }

                return PrivateFieldInOperator(privateId.Name, rightTarget, context);
            }

            var left = EvaluateExpression(expression.Left, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            switch (expression.Operator)
            {
                case "&&":
                    return IsTruthy(left)
                        ? EvaluateExpression(expression.Right, environment, context)
                        : left;
                case "||":
                    return IsTruthy(left)
                        ? left
                        : EvaluateExpression(expression.Right, environment, context);
                case "??":
                    return IsNullish(left)
                        ? EvaluateExpression(expression.Right, environment, context)
                        : left;
            }

            var right = EvaluateExpression(expression.Right, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            return expression.Operator switch
            {
                "+" => Add(left, right, context),
                "-" => Subtract(left, right, context),
                "*" => Multiply(left, right, context),
                "/" => Divide(left, right, context),
                "%" => Modulo(left, right, context),
                "**" => Power(left, right, context),
                "==" => LooseEquals(left, right, context),
                "!=" => !LooseEquals(left, right, context),
                "===" => StrictEquals(left, right),
                "!==" => !StrictEquals(left, right),
                "<" => JsOps.LessThan(left, right, context),
                "<=" => JsOps.LessThanOrEqual(left, right, context),
                ">" => JsOps.GreaterThan(left, right, context),
                ">=" => JsOps.GreaterThanOrEqual(left, right, context),
                "&" => BitwiseAnd(left, right, context),
                "|" => BitwiseOr(left, right, context),
                "^" => BitwiseXor(left, right, context),
                "<<" => LeftShift(left, right, context),
                ">>" => RightShift(left, right, context),
                ">>>" => UnsignedRightShift(left, right, context),
                "in" => InOperator(left, right, context),
                "instanceof" => InstanceofOperator(left, right, context),
                _ => throw new NotSupportedException($"Operator '{expression.Operator}' is not supported yet.")
            };
        }
    }

    /// <summary>
    /// Implements the ES2022 "Ergonomic brand checks for private fields" feature.
    /// The #field in obj expression checks if obj has the private field #field.
    /// </summary>
    private static bool PrivateFieldInOperator(string privateName, object? target, EvaluationContext context)
    {
        // Per ECMA-262 §13.10.2, the right-hand side of 'in' must be an object
        if (target is not JsObject jsObject)
        {
            context.SetThrow(StandardLibrary.CreateTypeError(
                "Cannot use 'in' operator to search for a private field in a non-object",
                context,
                context.RealmState));
            return false;
        }

        // Resolve the private name through the current private name scope
        // The privateName comes as just the identifier (e.g., "field"), we need to add # prefix
        var lexeme = $"#{privateName}";
        var resolvedKey = context.ResolvePrivateNameKey(lexeme);

        if (resolvedKey is null)
        {
            // Private name not found in current scope - this shouldn't happen for valid code
            // but if it does, the field is definitely not present
            return false;
        }

        // Check if the object has the private field with the resolved key
        return jsObject.HasPrivateField(resolvedKey);
    }
}
