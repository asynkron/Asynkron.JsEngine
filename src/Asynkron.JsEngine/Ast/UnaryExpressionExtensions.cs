using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(UnaryExpression expression)
    {
        private JsValue EvaluateUnary(JsEnvironment environment,
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
                            (e, env, ctx) => EvaluateExpression(e, env, ctx).ToObject());
                        var currentValue = reference.GetValue();

                        // Fast path for double (most common case) - avoid boxing
                        if (currentValue is double d)
                        {
                            var updatedValue = d + (expression.Operator == "++" ? 1 : -1);
                            reference.SetValue(updatedValue);
                            return expression.IsPrefix ? new JsValue(updatedValue) : new JsValue(d);
                        }

                        // Per ES spec, convert to numeric first (handles both Number and BigInt)
                        var oldNumeric = JsOps.ToNumeric(currentValue, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.FromObject(context.FlowValue);
                        }

                        // Calculate new value (increment/decrement the already-converted numeric value)
                        var updated = expression.Operator == "++"
                            ? IncrementValue(JsValue.FromObject(oldNumeric), context)
                            : DecrementValue(JsValue.FromObject(oldNumeric), context);
                        reference.SetValue(updated.ToObject());

                        // Postfix returns the old numeric value (after conversion but before increment/decrement)
                        // Prefix returns the new value (after increment/decrement)
                        return expression.IsPrefix ? updated : JsValue.FromObject(oldNumeric);
                    }
                    case "delete":
                        return EvaluateDelete(expression.Operand, environment, context) ? JsValue.True : JsValue.False;
                    case "typeof":
                    {
                        // The typeof operator has special semantics: it returns "undefined"
                        // for UNDECLARED references instead of throwing ReferenceError.
                        // However, it MUST throw for bindings in the Temporal Dead Zone (TDZ).
                        // Property getters MUST be invoked during evaluation (ES2024 13.5.3).

                        // For simple identifiers, check if the binding exists to distinguish
                        // between undeclared variables and TDZ variables
                        if (expression.Operand is IdentifierExpression identifier)
                        {
                            // Check if this identifier has a binding (even if uninitialized)
                            var hasBinding = environment.HasBinding(identifier.Name);

                            // Evaluate the operand (which will throw if in TDZ)
                            var operandValue = EvaluateExpression(expression.Operand, environment, context);

                            // If evaluation threw a ReferenceError
                            if (context.IsThrow)
                            {
                                // Only suppress the error if the variable was truly undeclared
                                // (no binding exists). If a binding exists, it's a TDZ error
                                // and should propagate.
                                if (!hasBinding)
                                {
                                    // Clear the error and return "undefined" for undeclared variables
                                    context.Clear();
                                    return new JsValue("undefined");
                                }

                                // Let TDZ errors propagate
                                return JsValue.Undefined;
                            }

                            if (context.ShouldStopEvaluation)
                            {
                                return JsValue.Undefined;
                            }

                            return new JsValue(GetTypeofStringValue(operandValue));
                        }

                        // For non-identifier operands, evaluate normally
                        var value = EvaluateExpression(expression.Operand, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        return new JsValue(GetTypeofStringValue(value));
                    }
                }

                var operand = EvaluateExpression(expression.Operand, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                return expression.Operator switch
                {
                    "!" => operand.IsTruthy ? JsValue.False : JsValue.True,
                    "+" => operand.IsBigInt
                        ? throw StandardLibrary.ThrowTypeError("Cannot convert a BigInt value to a number", context)
                        : new JsValue(ToNumberValue(operand, context)),
                    "-" => NegateValue(operand, context),
                    "~" => BitwiseNotValue(operand, context),
                    "void" => JsValue.Undefined,
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
                return JsValue.FromObject(errorObject);
            }
        }
    }

    /// <summary>
    /// Gets the typeof string for a JsValue.
    /// </summary>
    private static string GetTypeofStringValue(JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "object",
            JsValueKind.Boolean => "boolean",
            JsValueKind.Number => "number",
            JsValueKind.BigInt => "bigint",
            JsValueKind.String => "string",
            JsValueKind.Symbol => "symbol",
            JsValueKind.Object => GetTypeofStringForObject(value.ObjectValue),
            _ => "undefined"
        };
    }

    /// <summary>
    /// Gets the typeof string for an object value.
    /// Per ES spec, Proxy is "function" only if its target implements [[Call]].
    /// </summary>
    private static string GetTypeofStringForObject(object? obj)
    {
        // For Proxy, check if the target is callable (ES spec: Proxy has [[Call]] only if target does)
        if (obj is JsProxy proxy)
        {
            return proxy.Target is IJsCallable ? "function" : "object";
        }

        return obj is IJsCallable ? "function" : "object";
    }

    /// <summary>
    /// Bitwise NOT of a JsValue operand.
    /// </summary>
    private static JsValue BitwiseNotValue(JsValue operand, EvaluationContext context)
    {
        if (operand.IsNumber)
        {
            return new JsValue((double)~Converters.JsNumericConversions.ToInt32(operand.NumberValue));
        }

        return JsValue.FromObject(BitwiseNot(operand.ToObject(), context));
    }
}
