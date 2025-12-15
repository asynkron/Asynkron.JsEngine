using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(BinaryExpression expression)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateBinary(JsEnvironment environment,
            EvaluationContext context)
        {
            // ES2022: Handle private identifier in 'in' operator (#field in obj)
            if (expression.Operator == "in" && expression.Left is PrivateIdentifierExpression privateId)
            {
                var rightTarget = EvaluateExpression(expression.Right, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                return PrivateFieldInOperator(privateId.Name, rightTarget.ToObject(), context) ? JsValue.True : JsValue.False;
            }

            var left = EvaluateExpression(expression.Left, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            switch (expression.Operator)
            {
                case "&&":
                    return left.IsTruthy
                        ? EvaluateExpression(expression.Right, environment, context)
                        : left;
                case "||":
                    return left.IsTruthy
                        ? left
                        : EvaluateExpression(expression.Right, environment, context);
                case "??":
                    return left.IsNullOrUndefined
                        ? EvaluateExpression(expression.Right, environment, context)
                        : left;
            }

            var right = EvaluateExpression(expression.Right, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            return expression.Operator switch
            {
                "+" => AddValue(left, right, context),
                "-" => SubtractValue(left, right, context),
                "*" => MultiplyValue(left, right, context),
                "/" => DivideValue(left, right, context),
                "%" => ModuloValue(left, right, context),
                "**" => PowerValue(left, right, context),
                "==" => LooseEqualsValue(left, right, context) ? JsValue.True : JsValue.False,
                "!=" => LooseEqualsValue(left, right, context) ? JsValue.False : JsValue.True,
                "===" => StrictEqualsValue(left, right) ? JsValue.True : JsValue.False,
                "!==" => StrictEqualsValue(left, right) ? JsValue.False : JsValue.True,
                "<" => LessThanValue(left, right, context),
                "<=" => LessThanOrEqualValue(left, right, context),
                ">" => GreaterThanValue(left, right, context),
                ">=" => GreaterThanOrEqualValue(left, right, context),
                "&" => BitwiseAndValue(left, right, context),
                "|" => BitwiseOrValue(left, right, context),
                "^" => BitwiseXorValue(left, right, context),
                "<<" => LeftShiftValue(left, right, context),
                ">>" => RightShiftValue(left, right, context),
                ">>>" => UnsignedRightShiftValue(left, right, context),
                "in" => InOperator(left.ToObject(), right.ToObject(), context) ? JsValue.True : JsValue.False,
                "instanceof" => InstanceofOperator(left.ToObject(), right.ToObject(), context) ? JsValue.True : JsValue.False,
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
            context.SetThrow(JsValue.FromObject(StandardLibrary.CreateTypeError(
                "Cannot use 'in' operator to search for a private field in a non-object",
                context,
                context.RealmState)));
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

        // Check if the object has the private field with the resolved key (for private fields)
        if (jsObject.HasPrivateField(resolvedKey))
        {
            return true;
        }

        // For private methods and accessors, we need to check the brand
        // The brand is associated with the PrivateNameScope
        if (PrivateNameScope.TryResolveScope(resolvedKey, out var scope) && scope is not null)
        {
            return jsObject.HasPrivateBrand(scope.BrandToken);
        }

        return false;
    }
}
