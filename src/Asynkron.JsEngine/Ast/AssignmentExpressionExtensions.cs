using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(AssignmentExpression expression)
    {
        private JsValue EvaluateAssignment(JsEnvironment environment,
            EvaluationContext context)
        {
            var reference = AssignmentReferenceResolver.Resolve(
                new IdentifierExpression(expression.Source, expression.Target), environment, context,
                (e, env, ctx) => EvaluateExpression(e, env, ctx).ToObject());

            // Use JsValue version of compound assignment to avoid boxing
            if (expression.IsCompoundAssignment &&
                TryEvaluateCompoundAssignmentJsValue(expression, expression.Value, reference, environment, context,
                    out var compoundJsValue,
                    out var shouldAssignCompound))
            {
                if (context.ShouldStopEvaluation)
                {
                    return compoundJsValue;
                }

                if (shouldAssignCompound)
                {
                    reference.SetValue(compoundJsValue);
                }

                return compoundJsValue;
            }

            var targetValue = EvaluateAssignmentRhsWithNameHint(expression, expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.FromObject(targetValue);
            }

            try
            {
                reference.SetValue(JsValue.FromObject(targetValue));
                return JsValue.FromObject(targetValue);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                           StringComparison.Ordinal))
            {
                object? errorObject = ex.Message;

                // If a ReferenceError constructor is available, use it to
                // create a proper JS error instance so user code can catch
                // and inspect it.
                if (environment.TryGet(Symbol.ReferenceErrorIdentifier, out var ctor) &&
                    ctor is IJsCallable callable)
                {
                    errorObject = callable.Invoke([JsValue.FromObject(ex.Message)], JsValue.Undefined).ToObject();
                }

                context.SetThrow(errorObject);
                return JsValue.FromObject(errorObject);
            }
        }
    }

    private static bool IsParenthesizedIdentifierAssignment(AssignmentExpression expression)
    {
        if (expression.Source is null)
        {
            return false;
        }

        // Heuristic: if the identifier token is immediately preceded (ignoring
        // whitespace) by a '(', it came from a CoverParenthesizedExpression
        // and should not trigger SetFunctionName inference.
        var source = expression.Source.Source;
        var index = expression.Source.StartPosition - 1;
        while (index >= 0 && char.IsWhiteSpace(source, index))
        {
            index--;
        }

        return index >= 0 && source[index] == '(';
    }

    /// <summary>
    /// JsValue version of compound assignment evaluation that avoids boxing for numeric operations.
    /// </summary>
    private static bool TryEvaluateCompoundAssignmentJsValue(
        AssignmentExpression? assignment,
        ExpressionNode candidate,
        AssignmentReference reference,
        JsEnvironment environment,
        EvaluationContext context,
        out JsValue value,
        out bool shouldAssign)
    {
        if (candidate is not BinaryExpression binary)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return false;
        }

        // Use GetJsValue() to avoid boxing for declarative bindings
        var leftJs = reference.GetJsValue();
        if (context.ShouldStopEvaluation)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return true;
        }

        switch (binary.Operator)
        {
            case "&&":
                if (!leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
            case "||":
                if (leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
            case "??":
                if (!leftJs.IsNullish)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
        }

        var rightJs = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
        if (context.ShouldStopEvaluation)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return true;
        }

        // Use JsValue arithmetic operations to avoid boxing
        value = binary.Operator switch
        {
            "+" => AddValue(leftJs, rightJs, context),
            "-" => SubtractValue(leftJs, rightJs, context),
            "*" => MultiplyValue(leftJs, rightJs, context),
            "/" => DivideValue(leftJs, rightJs, context),
            "%" => ModuloValue(leftJs, rightJs, context),
            "**" => PowerValue(leftJs, rightJs, context),
            "==" => LooseEqualsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            "!=" => !LooseEqualsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            "===" => StrictEqualsValue(leftJs, rightJs) ? JsValue.True : JsValue.False,
            "!==" => !StrictEqualsValue(leftJs, rightJs) ? JsValue.True : JsValue.False,
            "<" => LessThanValue(leftJs, rightJs, context),
            "<=" => LessThanOrEqualValue(leftJs, rightJs, context),
            ">" => GreaterThanValue(leftJs, rightJs, context),
            ">=" => GreaterThanOrEqualValue(leftJs, rightJs, context),
            "&" => BitwiseAndValue(leftJs, rightJs, context),
            "|" => BitwiseOrValue(leftJs, rightJs, context),
            "^" => BitwiseXorValue(leftJs, rightJs, context),
            "<<" => LeftShiftValue(leftJs, rightJs, context),
            ">>" => RightShiftValue(leftJs, rightJs, context),
            ">>>" => UnsignedRightShiftValue(leftJs, rightJs, context),
            "in" => InOperator(leftJs.ToObject(), rightJs.ToObject(), context) ? JsValue.True : JsValue.False,
            "instanceof" => InstanceofOperator(leftJs.ToObject(), rightJs.ToObject(), context) ? JsValue.True : JsValue.False,
            _ => throw new NotSupportedException(
                $"Compound assignment operator '{binary.Operator}' is not supported yet.")
        };
        shouldAssign = true;

        return true;
    }

    private static JsValue EvaluateAssignmentRhsWithNameHintJsValue(
        AssignmentExpression? assignment,
        ExpressionNode rhs,
        JsEnvironment environment,
        EvaluationContext context)
    {
        using var functionNameHint = ShouldApplyAssignmentNameHint(assignment, rhs)
            ? context.EnterFunctionNameHint(assignment!.Target)
            : null;

        var jsValue = EvaluateExpression(rhs, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return jsValue;
        }

        if (assignment is not null &&
            jsValue.ObjectValue is IFunctionNameTarget nameTarget &&
            IsAnonymousFunctionDefinitionNode(rhs) &&
            !IsParenthesizedIdentifierAssignment(assignment))
        {
            nameTarget.EnsureHasName(assignment.Target.Name);
        }

        return jsValue;
    }

    private static bool TryEvaluateCompoundAssignmentValue(
        AssignmentExpression? assignment,
        ExpressionNode candidate,
        AssignmentReference reference,
        JsEnvironment environment,
        EvaluationContext context,
        out object? value,
        out bool shouldAssign)
    {
        if (candidate is not BinaryExpression binary)
        {
            value = null;
            shouldAssign = false;
            return false;
        }

        var leftValue = reference.GetValue();
        if (context.ShouldStopEvaluation)
        {
            value = Symbol.Undefined;
            shouldAssign = false;
            return true;
        }

        switch (binary.Operator)
        {
            case "&&":
                if (!IsTruthy(leftValue))
                {
                    value = leftValue;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHint(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
            case "||":
                if (IsTruthy(leftValue))
                {
                    value = leftValue;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHint(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
            case "??":
                if (!IsNullish(leftValue))
                {
                    value = leftValue;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHint(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
        }

        var rightValue = EvaluateAssignmentRhsWithNameHint(assignment, binary.Right, environment, context);
        if (context.ShouldStopEvaluation)
        {
            value = Symbol.Undefined;
            shouldAssign = false;
            return true;
        }

        value = binary.Operator switch
        {
            "+" => Add(leftValue, rightValue, context),
            "-" => Subtract(leftValue, rightValue, context),
            "*" => Multiply(leftValue, rightValue, context),
            "/" => Divide(leftValue, rightValue, context),
            "%" => Modulo(leftValue, rightValue, context),
            "**" => Power(leftValue, rightValue, context),
            "==" => LooseEquals(leftValue, rightValue, context),
            "!=" => !LooseEquals(leftValue, rightValue, context),
            "===" => StrictEquals(leftValue, rightValue),
            "!==" => !StrictEquals(leftValue, rightValue),
            "<" => JsOps.LessThan(leftValue, rightValue, context),
            "<=" => JsOps.LessThanOrEqual(leftValue, rightValue, context),
            ">" => JsOps.GreaterThan(leftValue, rightValue, context),
            ">=" => JsOps.GreaterThanOrEqual(leftValue, rightValue, context),
            "&" => BitwiseAnd(leftValue, rightValue, context),
            "|" => BitwiseOr(leftValue, rightValue, context),
            "^" => BitwiseXor(leftValue, rightValue, context),
            "<<" => LeftShift(leftValue, rightValue, context),
            ">>" => RightShift(leftValue, rightValue, context),
            ">>>" => UnsignedRightShift(leftValue, rightValue, context),
            "in" => InOperator(leftValue, rightValue, context),
            "instanceof" => InstanceofOperator(leftValue, rightValue, context),
            _ => throw new NotSupportedException(
                $"Compound assignment operator '{binary.Operator}' is not supported yet.")
        };
        shouldAssign = true;

        return true;
    }

    private static object? EvaluateAssignmentRhsWithNameHint(
        AssignmentExpression? assignment,
        ExpressionNode rhs,
        JsEnvironment environment,
        EvaluationContext context)
    {
        using var functionNameHint = ShouldApplyAssignmentNameHint(assignment, rhs)
            ? context.EnterFunctionNameHint(assignment.Target)
            : null;

        var jsValue = EvaluateExpression(rhs, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return jsValue.ToObject();
        }

        var value = jsValue.ToObject();
        if (assignment is not null &&
            value is IFunctionNameTarget nameTarget &&
            IsAnonymousFunctionDefinitionNode(rhs) &&
            !IsParenthesizedIdentifierAssignment(assignment))
        {
            nameTarget.EnsureHasName(assignment.Target.Name);
        }

        return value;
    }

    private static bool ShouldApplyAssignmentNameHint(AssignmentExpression? assignment, ExpressionNode rhs)
    {
        return assignment is not null &&
               IsAnonymousFunctionDefinitionNode(rhs) &&
               !IsParenthesizedIdentifierAssignment(assignment);
    }
}
