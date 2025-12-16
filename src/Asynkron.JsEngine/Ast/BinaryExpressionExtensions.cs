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
            if (expression.Operator == BinaryOperator.In && expression.Left is PrivateIdentifierExpression privateId)
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
                case BinaryOperator.LogicalAnd:
                    return left.IsTruthy
                        ? EvaluateExpression(expression.Right, environment, context)
                        : left;
                case BinaryOperator.LogicalOr:
                    return left.IsTruthy
                        ? left
                        : EvaluateExpression(expression.Right, environment, context);
                case BinaryOperator.NullishCoalescing:
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
                BinaryOperator.Add => AddValue(left, right, context),
                BinaryOperator.Subtract => SubtractValue(left, right, context),
                BinaryOperator.Multiply => MultiplyValue(left, right, context),
                BinaryOperator.Divide => DivideValue(left, right, context),
                BinaryOperator.Modulo => ModuloValue(left, right, context),
                BinaryOperator.Power => PowerValue(left, right, context),
                BinaryOperator.Equal => LooseEqualsValue(left, right, context) ? JsValue.True : JsValue.False,
                BinaryOperator.NotEqual => LooseEqualsValue(left, right, context) ? JsValue.False : JsValue.True,
                BinaryOperator.StrictEqual => StrictEqualsValue(left, right) ? JsValue.True : JsValue.False,
                BinaryOperator.StrictNotEqual => StrictEqualsValue(left, right) ? JsValue.False : JsValue.True,
                BinaryOperator.LessThan => LessThanValue(left, right, context),
                BinaryOperator.LessThanOrEqual => LessThanOrEqualValue(left, right, context),
                BinaryOperator.GreaterThan => GreaterThanValue(left, right, context),
                BinaryOperator.GreaterThanOrEqual => GreaterThanOrEqualValue(left, right, context),
                BinaryOperator.BitwiseAnd => BitwiseAndValue(left, right, context),
                BinaryOperator.BitwiseOr => BitwiseOrValue(left, right, context),
                BinaryOperator.BitwiseXor => BitwiseXorValue(left, right, context),
                BinaryOperator.LeftShift => LeftShiftValue(left, right, context),
                BinaryOperator.RightShift => RightShiftValue(left, right, context),
                BinaryOperator.UnsignedRightShift => UnsignedRightShiftValue(left, right, context),
                BinaryOperator.In => InOperator(left.ToObject(), right.ToObject(), context) ? JsValue.True : JsValue.False,
                BinaryOperator.InstanceOf => InstanceofOperator(left.ToObject(), right.ToObject(), context) ? JsValue.True : JsValue.False,
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
