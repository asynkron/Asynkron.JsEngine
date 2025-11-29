using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(AssignmentExpression expression)
    {
        private object? EvaluateAssignment(JsEnvironment environment,
            EvaluationContext context)
        {
            var reference = AssignmentReferenceResolver.Resolve(
                new IdentifierExpression(expression.Source, expression.Target), environment, context,
                EvaluateExpression);

            if (expression.IsCompoundAssignment &&
                TryEvaluateCompoundAssignmentValue(expression.Value, reference, environment, context, out var compoundValue))
            {
                if (context.ShouldStopEvaluation)
                {
                    return compoundValue;
                }

                reference.SetValue(compoundValue);
                return compoundValue;
            }

            var targetValue = EvaluateExpression(expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return targetValue;
            }

            try
            {
                reference.SetValue(targetValue);
                return targetValue;
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
                    errorObject = callable.Invoke([ex.Message], Symbol.Undefined);
                }

                context.SetThrow(errorObject);
                return errorObject;
            }
        }
    }

    private static bool TryEvaluateCompoundAssignmentValue(
        ExpressionNode candidate,
        AssignmentReference reference,
        JsEnvironment environment,
        EvaluationContext context,
        out object? value)
    {
        if (candidate is not BinaryExpression binary)
        {
            value = null;
            return false;
        }

        var leftValue = reference.GetValue();
        if (context.ShouldStopEvaluation)
        {
            value = Symbol.Undefined;
            return true;
        }

        switch (binary.Operator)
        {
            case "&&":
                if (!IsTruthy(leftValue))
                {
                    value = leftValue;
                    return true;
                }

                value = EvaluateExpression(binary.Right, environment, context);
                return true;
            case "||":
                if (IsTruthy(leftValue))
                {
                    value = leftValue;
                    return true;
                }

                value = EvaluateExpression(binary.Right, environment, context);
                return true;
            case "??":
                if (!IsNullish(leftValue))
                {
                    value = leftValue;
                    return true;
                }

                value = EvaluateExpression(binary.Right, environment, context);
                return true;
        }

        var rightValue = EvaluateExpression(binary.Right, environment, context);
        if (context.ShouldStopEvaluation)
        {
            value = Symbol.Undefined;
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

        return true;
    }
}
