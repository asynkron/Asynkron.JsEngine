#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// JsValue overload for private field 'in' operator - avoids boxing.
    /// </summary>
    private static bool PrivateFieldInOperatorJsValue(string privateName, in JsValue target, EvaluationContext context)
    {
        // Per ECMA-262 §13.10.2, the right-hand side of 'in' must be an object
        if (target.Kind != JsValueKind.Object || target.ObjectValue is not JsObject jsObject)
        {
            context.SetThrow(StandardLibrary.CreateTypeError(
                "Cannot use 'in' operator to search for a private field in a non-object",
                context,
                context.RealmState));
            return false;
        }

        // Resolve the private name through the current private name scope
        var lexeme = $"#{privateName}";
        var resolvedKey = context.ResolvePrivateNameKey(lexeme);

        if (resolvedKey is null)
        {
            return false;
        }

        if (jsObject.HasPrivateField(resolvedKey))
        {
            return true;
        }

        if (PrivateNameScope.TryResolveScope(context.RealmState, resolvedKey, out var scope) && scope is not null)
        {
            return jsObject.HasPrivateBrand(scope.BrandToken);
        }

        return false;
    }

    extension(BinaryExpression expression)
    {
        /// <summary>
        /// Hot path for binary expressions - handles common arithmetic and comparison operators.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateBinary(JsEnvironment environment,
            EvaluationContext context)
        {
            // Hot path: handle common operators directly
            var op = expression.Operator;

            switch (op)
            {
                // Logical operators evaluate left first and may short-circuit
                case BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr or BinaryOperator.NullishCoalescing:
                    {
                        var left = expression.Left.EvaluateExpression(environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        return op switch
                        {
                            BinaryOperator.LogicalAnd => left.IsTruthy
                                ? expression.Right.EvaluateExpression(environment, context)
                                : left,
                            BinaryOperator.LogicalOr => left.IsTruthy
                                ? left
                                : expression.Right.EvaluateExpression(environment, context),
                            _ => left.IsNullOrUndefined ? expression.Right.EvaluateExpression(environment, context) : left
                        };
                        // NullishCoalescing
                    }
                // Slow path: private field In operator, In, InstanceOf
                case BinaryOperator.In:
                case BinaryOperator.InstanceOf:
                    return expression.EvaluateBinarySlow(environment, context);
            }

            // Common case: evaluate both operands then apply operator
            var leftVal = expression.Left.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var rightVal = expression.Right.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            return op switch
            {
                // Hot path operators - most common in loops and calculations
                BinaryOperator.Add => AddValue(leftVal, rightVal, context),
                BinaryOperator.Subtract => SubtractValue(leftVal, rightVal, context),
                BinaryOperator.LessThan => LessThanValue(leftVal, rightVal, context),
                BinaryOperator.LessThanOrEqual => LessThanOrEqualValue(leftVal, rightVal, context),
                BinaryOperator.StrictEqual => StrictEqualsValue(leftVal, rightVal) ? JsValue.True : JsValue.False,
                BinaryOperator.StrictNotEqual => StrictEqualsValue(leftVal, rightVal) ? JsValue.False : JsValue.True,
                _ => BinaryExpression.EvaluateBinaryOperator(op, leftVal, rightVal, context)
            };

            // Medium frequency operators
        }

        /// <summary>
        /// Handles medium frequency binary operators.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static JsValue EvaluateBinaryOperator(BinaryOperator op, in JsValue left, in JsValue right,
            EvaluationContext context)
        {
            return op switch
            {
                BinaryOperator.Multiply => MultiplyValue(left, right, context),
                BinaryOperator.Divide => DivideValue(left, right, context),
                BinaryOperator.Modulo => ModuloValue(left, right, context),
                BinaryOperator.Power => PowerValue(left, right, context),
                BinaryOperator.Equal => LooseEqualsValue(left, right, context) ? JsValue.True : JsValue.False,
                BinaryOperator.NotEqual => LooseEqualsValue(left, right, context) ? JsValue.False : JsValue.True,
                BinaryOperator.GreaterThan => GreaterThanValue(left, right, context),
                BinaryOperator.GreaterThanOrEqual => GreaterThanOrEqualValue(left, right, context),
                BinaryOperator.BitwiseAnd => BitwiseAndValue(left, right, context),
                BinaryOperator.BitwiseOr => BitwiseOrValue(left, right, context),
                BinaryOperator.BitwiseXor => BitwiseXorValue(left, right, context),
                BinaryOperator.LeftShift => LeftShiftValue(left, right, context),
                BinaryOperator.RightShift => RightShiftValue(left, right, context),
                BinaryOperator.UnsignedRightShift => UnsignedRightShiftValue(left, right, context),
                _ => throw new NotSupportedException($"Operator '{op}' is not supported yet.")
            };
        }

        /// <summary>
        /// Slow path for In and InstanceOf operators.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private JsValue EvaluateBinarySlow(JsEnvironment environment, EvaluationContext context)
        {
            // ES2022: Handle private identifier in 'in' operator (#field in obj)
            if (expression is { Operator: BinaryOperator.In, Left: PrivateIdentifierExpression privateId })
            {
                var rightTarget = expression.Right.EvaluateExpression(environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                return PrivateFieldInOperatorJsValue(privateId.Name, rightTarget, context)
                    ? JsValue.True
                    : JsValue.False;
            }

            var left = expression.Left.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var right = expression.Right.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            return expression.Operator switch
            {
                BinaryOperator.In => InOperatorJsValue(left, right, context) ? JsValue.True : JsValue.False,
                BinaryOperator.InstanceOf => InstanceofOperatorJsValue(left, right, context)
                    ? JsValue.True
                    : JsValue.False,
                _ => JsValue.Undefined
            };
        }
    }
}
