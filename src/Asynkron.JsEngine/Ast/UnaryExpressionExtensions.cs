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
            try
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

                        // Per ES spec, convert to numeric first (handles both Number and BigInt)
                        var oldNumeric = JsOps.ToNumeric(currentValue, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return context.FlowValue;
                        }

                        // Calculate new value (increment/decrement the already-converted numeric value)
                        var updatedValue = expression.Operator == "++"
                            ? IncrementValue(oldNumeric, context)
                            : DecrementValue(oldNumeric, context);
                        reference.SetValue(updatedValue);

                        // Postfix returns the old numeric value (after conversion but before increment/decrement)
                        // Prefix returns the new value (after increment/decrement)
                        return expression.IsPrefix ? updatedValue : oldNumeric;
                    }
                    case "delete":
                        return EvaluateDelete(expression.Operand, environment, context);
                    case "typeof":
                    {
                        // The typeof operator has special semantics: it returns "undefined"
                        // for unresolvable references instead of throwing ReferenceError.
                        // Property getters MUST be invoked during evaluation (ES2024 13.5.3).

                        // Always evaluate the operand to invoke GetValue (which triggers property getters)
                        var operandValue = EvaluateExpression(expression.Operand, environment, context);

                        // If evaluation resulted in a ReferenceError for a simple identifier,
                        // typeof should return "undefined" instead of propagating the error
                        if (context.IsThrow && expression.Operand is IdentifierExpression)
                        {
                            // Clear the error and return "undefined" for unresolvable references
                            context.Clear();
                            return "undefined";
                        }

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
                    "-" => UnaryMinus(operand, context),
                    "~" => BitwiseNot(operand, context),
                    "void" => Symbol.Undefined,
                    _ => throw new NotSupportedException($"Operator '{expression.Operator}' is not supported yet.")
                };
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                         StringComparison.Ordinal))
            {
                object? errorObject = ex.Message;

                if (environment.TryGet(Symbol.ReferenceErrorIdentifier, out var ctor) &&
                    ctor is IJsCallable callable)
                {
                    try
                    {
                        errorObject = callable.Invoke([ex.Message], Symbol.Undefined);
                    }
                    catch (ThrowSignal signal)
                    {
                        errorObject = signal.ThrownValue;
                    }
                }

                context.SetThrow(errorObject);
                return errorObject;
            }
        }
    }
}
