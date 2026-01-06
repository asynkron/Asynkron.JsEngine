#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Gets the typeof string for a JsValue.
    /// </summary>
    private static string GetTypeofStringValue(in JsValue value)
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
    private static JsValue BitwiseNotValue(in JsValue operand, EvaluationContext context)
    {
        // Use the JsValue overload that handles all types without boxing
        return BitwiseNotJsValue(in operand, context);
    }

    /// <summary>
    /// Hot path for unary expressions - handles common operators without try/catch.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue EvaluateUnary(this UnaryExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var op = expression.Operator;

        // Hot path: increment/decrement on simple identifiers with slots
        if (op == UnaryOperator.Increment || op == UnaryOperator.Decrement)
        {
            var targetOperand = expression.Operand;
            while (targetOperand is UnaryExpression
                {
                    Operator: UnaryOperator.Increment or UnaryOperator.Decrement
                } nested)
            {
                targetOperand = nested.Operand;
            }

            if (targetOperand is IdentifierExpression identifier)
            {
                // When there's a with object in the chain, we must use reference-based approach
                // to preserve the binding object for PutValue even if property is deleted during GetValue
                if (environment.HasWithObjectInChain())
                {
                    return expression.EvaluateUnaryMemberIncrement(environment, context);
                }

                // Fastest path: slot-based access
                if (identifier is { SlotIndex: >= 0, ScopeId: >= 0 } &&
                    environment.TryReadIdentifierWithSlot(identifier, context, out var slotValue))
                {
                    if (context.ShouldStopEvaluation)
                    {
                        return context.FlowValue;
                    }

                    // Fast path for Number (most common case in loops)
                    if (slotValue.Kind == JsValueKind.Number)
                    {
                        var d = slotValue.NumberValue;
                        var updatedValue = d + (op == UnaryOperator.Increment ? 1 : -1);
                        var updatedJs = new JsValue(updatedValue);
                        environment.TryWriteIdentifierWithSlot(identifier, updatedJs, context);
                        return expression.IsPrefix ? updatedJs : new JsValue(d);
                    }

                    // Non-numeric slot values
                    var slotOldNumeric = ToNumericValue(slotValue, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return context.FlowValue;
                    }

                    var slotUpdated = op == UnaryOperator.Increment
                        ? IncrementValue(slotOldNumeric, context)
                        : DecrementValue(slotOldNumeric, context);
                    environment.TryWriteIdentifierWithSlot(identifier, slotUpdated, context);
                    return expression.IsPrefix ? slotUpdated : slotOldNumeric;
                }

                // Direct identifier path (no slot)
                var currentJsValue = context.GetIdentifier(environment, identifier.Name);
                if (context.ShouldStopEvaluation)
                {
                    return context.FlowValue;
                }

                if (currentJsValue.Kind == JsValueKind.Number)
                {
                    var d = currentJsValue.NumberValue;
                    var updatedValue = d + (op == UnaryOperator.Increment ? 1 : -1);
                    environment.SetIdentifierJsValue(identifier.Name, new JsValue(updatedValue), context);
                    return expression.IsPrefix ? new JsValue(updatedValue) : new JsValue(d);
                }

                var oldNumeric = ToNumericValue(currentJsValue, context);
                if (context.ShouldStopEvaluation)
                {
                    return context.FlowValue;
                }

                var updated = op == UnaryOperator.Increment
                    ? IncrementValue(oldNumeric, context)
                    : DecrementValue(oldNumeric, context);
                environment.SetIdentifierJsValue(identifier.Name, updated, context);
                return expression.IsPrefix ? updated : oldNumeric;
            }

            // Member expression increment/decrement - slow path with try/catch
            return expression.EvaluateUnaryMemberIncrement(environment, context);
        }

        // Hot path: logical not
        if (op == UnaryOperator.LogicalNot)
        {
            var operand = expression.Operand.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            return operand.IsTruthy ? JsValue.False : JsValue.True;
        }

        // Other operators - slow path
        return expression.EvaluateUnarySlow(environment, context);
    }

    /// <summary>
    /// Slow path for member expression increment/decrement - requires try/catch for ReferenceError.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue EvaluateUnaryMemberIncrement(this UnaryExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        try
        {
            var reference = AssignmentReferenceResolver.Resolve(
                expression.Operand,
                environment,
                context,
                static (e, env, ctx) => e.EvaluateExpression(env, ctx));

            var refCurrentJsValue = reference.GetJsValue();

            if (refCurrentJsValue.Kind == JsValueKind.Number)
            {
                var d = refCurrentJsValue.NumberValue;
                var updatedValue = d + (expression.Operator == UnaryOperator.Increment ? 1 : -1);
                reference.SetValue(new JsValue(updatedValue));
                return expression.IsPrefix ? new JsValue(updatedValue) : new JsValue(d);
            }

            var refOldNumeric = ToNumericValue(refCurrentJsValue, context);
            if (context.ShouldStopEvaluation)
            {
                return context.FlowValue;
            }

            var refUpdated = expression.Operator == UnaryOperator.Increment
                ? IncrementValue(refOldNumeric, context)
                : DecrementValue(refOldNumeric, context);
            reference.SetValue(refUpdated);
            return expression.IsPrefix ? refUpdated : refOldNumeric;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                       StringComparison.Ordinal))
        {
            return expression.HandleReferenceErrorUnary(ex, environment, context);
        }
    }

    /// <summary>
    /// Slow path for other unary operators (typeof, delete, plus, minus, etc.).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue EvaluateUnarySlow(this UnaryExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        switch (expression.Operator)
        {
            case UnaryOperator.Delete:
                return expression.Operand.EvaluateDelete(environment, context) ? JsValue.True : JsValue.False;

            case UnaryOperator.Increment:
            case UnaryOperator.Decrement:
                // When fast paths are disabled, use the full reference-based implementation
                return expression.EvaluateUnaryMemberIncrement(environment, context);

            case UnaryOperator.TypeOf:
                {
                    if (expression.Operand is IdentifierExpression identifier)
                    {
                        var hasBinding = environment.HasBinding(identifier.Name);
                        var operandValue = expression.Operand.EvaluateExpression(environment, context);

                        if (context.IsThrow)
                        {
                            if (!hasBinding)
                            {
                                context.Clear();
                                return new JsValue("undefined");
                            }

                            return JsValue.Undefined;
                        }

                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        return new JsValue(GetTypeofStringValue(operandValue));
                    }

                    var value = expression.Operand.EvaluateExpression(environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    return new JsValue(GetTypeofStringValue(value));
                }
        }

        var operand = expression.Operand.EvaluateExpression(environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return expression.Operator switch
        {
            UnaryOperator.Plus => operand.IsBigInt
                ? throw StandardLibrary.ThrowTypeError("Cannot convert a BigInt value to a number", context)
                : new JsValue(ToNumberValue(operand, context)),
            UnaryOperator.Minus => NegateValue(operand, context),
            UnaryOperator.BitwiseNot => BitwiseNotValue(operand, context),
            UnaryOperator.Void => JsValue.Undefined,
            _ => throw new NotSupportedException($"Operator '{expression.Operator}' is not supported yet.")
        };
    }

    /// <summary>
    /// Handles ReferenceError exceptions from unary operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue HandleReferenceErrorUnary(this UnaryExpression _, InvalidOperationException ex, JsEnvironment environment,
        EvaluationContext context)
    {
        var errorValue = new JsValue(ex.Message);

        if (environment.TryGetObject<IJsCallable>(Symbol.ReferenceErrorIdentifier, out var callable))
        {
            try
            {
                errorValue = callable.Invoke(new SingleValueArgs(new JsValue(ex.Message)), JsValue.Undefined);
            }
            catch (ThrowSignal signal)
            {
                errorValue = signal.ThrownValue;
            }
        }

        context.SetThrow(errorValue);
        return errorValue;
    }
}
