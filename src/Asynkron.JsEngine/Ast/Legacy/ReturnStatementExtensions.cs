#region

using System.Runtime.CompilerServices;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Applies a binary operator to two JsValue operands.
    /// For logical operators (&&, ||, ??), short-circuit evaluation must be done by the caller;
    /// this method just returns the right operand for those cases.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue ApplyBinaryOperator(BinaryOperator op, JsValue left, JsValue right,
        EvaluationContext context)
    {
        return op switch
        {
            BinaryOperator.Add => AddValue(left, right, context),
            BinaryOperator.Subtract => SubtractValue(left, right, context),
            BinaryOperator.Multiply => MultiplyValue(left, right, context),
            BinaryOperator.Divide => DivideValue(left, right, context),
            BinaryOperator.Modulo => ModuloValue(left, right, context),
            BinaryOperator.Power => PowerValue(left, right, context),
            BinaryOperator.LessThan => LessThanValue(left, right, context),
            BinaryOperator.LessThanOrEqual => LessThanOrEqualValue(left, right, context),
            BinaryOperator.GreaterThan => GreaterThanValue(left, right, context),
            BinaryOperator.GreaterThanOrEqual => GreaterThanOrEqualValue(left, right, context),
            BinaryOperator.Equal => LooseEqualsValue(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.NotEqual => LooseEqualsValue(left, right, context) ? JsValue.False : JsValue.True,
            BinaryOperator.StrictEqual => StrictEqualsValue(left, right) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictNotEqual => StrictEqualsValue(left, right) ? JsValue.False : JsValue.True,
            BinaryOperator.BitwiseAnd => BitwiseAndValue(left, right, context),
            BinaryOperator.BitwiseOr => BitwiseOrValue(left, right, context),
            BinaryOperator.BitwiseXor => BitwiseXorValue(left, right, context),
            BinaryOperator.LeftShift => LeftShiftValue(left, right, context),
            BinaryOperator.RightShift => RightShiftValue(left, right, context),
            BinaryOperator.UnsignedRightShift => UnsignedRightShiftValue(left, right, context),
            BinaryOperator.In => InOperatorJsValue(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.InstanceOf => InstanceofOperatorJsValue(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr or BinaryOperator.NullishCoalescing => right,
            _ => throw new NotSupportedException($"Operator '{op}' is not supported.")
        };
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateReturnJsValue(this ReturnStatement statement, JsEnvironment environment,
        EvaluationContext context)
    {
        var jsValue = statement.Expression is null
            ? JsValue.Undefined
            : statement.Expression.EvaluateDynamicExpressionOperand(
                environment,
                context,
                "Dynamic return expression");

        if (context.ShouldStopEvaluation)
        {
            return jsValue;
        }

        context.SetReturn(jsValue);
        return jsValue;
    }
}
