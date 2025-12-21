using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
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

            // Logical operators evaluate left first and may short-circuit
            if (op == BinaryOperator.LogicalAnd || op == BinaryOperator.LogicalOr || op == BinaryOperator.NullishCoalescing)
            {
                var left = expression.Left.EvaluateExpression(environment, context);
                if (context.ShouldStopEvaluation)
                    return JsValue.Undefined;

                if (op == BinaryOperator.LogicalAnd)
                    return left.IsTruthy ? expression.Right.EvaluateExpression(environment, context) : left;
                if (op == BinaryOperator.LogicalOr)
                    return left.IsTruthy ? left : expression.Right.EvaluateExpression(environment, context);
                // NullishCoalescing
                return left.IsNullOrUndefined ? expression.Right.EvaluateExpression(environment, context) : left;
            }

            // Slow path: private field In operator, In, InstanceOf
            if (op == BinaryOperator.In || op == BinaryOperator.InstanceOf)
                return expression.EvaluateBinarySlow(environment, context);

            // Common case: evaluate both operands then apply operator
            var leftVal = expression.Left.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
                return JsValue.Undefined;

            var rightVal = expression.Right.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
                return JsValue.Undefined;

            // Hot path operators - most common in loops and calculations
            if (op == BinaryOperator.Add)
                return AddValue(leftVal, rightVal, context);
            if (op == BinaryOperator.Subtract)
                return SubtractValue(leftVal, rightVal, context);
            if (op == BinaryOperator.LessThan)
                return LessThanValue(leftVal, rightVal, context);
            if (op == BinaryOperator.LessThanOrEqual)
                return LessThanOrEqualValue(leftVal, rightVal, context);
            if (op == BinaryOperator.StrictEqual)
                return StrictEqualsValue(leftVal, rightVal) ? JsValue.True : JsValue.False;
            if (op == BinaryOperator.StrictNotEqual)
                return StrictEqualsValue(leftVal, rightVal) ? JsValue.False : JsValue.True;

            // Medium frequency operators
            return BinaryExpression.EvaluateBinaryOperator(op, leftVal, rightVal, context);
        }

        /// <summary>
        /// Handles medium frequency binary operators.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static JsValue EvaluateBinaryOperator(BinaryOperator op, JsValue left, JsValue right, EvaluationContext context)
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
            if (expression.Operator == BinaryOperator.In && expression.Left is PrivateIdentifierExpression privateId)
            {
                var rightTarget = expression.Right.EvaluateExpression(environment, context);
                if (context.ShouldStopEvaluation)
                    return JsValue.Undefined;

                return PrivateFieldInOperator(privateId.Name, rightTarget.ToObject(), context) ? JsValue.True : JsValue.False;
            }

            var left = expression.Left.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
                return JsValue.Undefined;

            var right = expression.Right.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
                return JsValue.Undefined;

            return expression.Operator switch
            {
                BinaryOperator.In => InOperator(left.ToObject(), right.ToObject(), context) ? JsValue.True : JsValue.False,
                BinaryOperator.InstanceOf => InstanceofOperator(left.ToObject(), right.ToObject(), context) ? JsValue.True : JsValue.False,
                _ => JsValue.Undefined
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
