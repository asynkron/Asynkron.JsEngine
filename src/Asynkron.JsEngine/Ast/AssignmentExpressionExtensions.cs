#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
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
    /// Fast path for compound assignment using direct identifier access (avoids AssignmentReference).
    /// </summary>
    private static bool TryEvaluateCompoundAssignmentDirectJsValue(
        AssignmentExpression? assignment,
        ExpressionNode candidate,
        Symbol target,
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

        // Use direct identifier access to avoid creating AssignmentReference
        var leftJs = context.GetIdentifier(environment, target);
        if (context.ShouldStopEvaluation)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return true;
        }

        switch (binary.Operator)
        {
            case BinaryOperator.LogicalAnd:
                if (!leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
            case BinaryOperator.LogicalOr:
                if (leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
            case BinaryOperator.NullishCoalescing:
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
            BinaryOperator.Add => AddValue(leftJs, rightJs, context),
            BinaryOperator.Subtract => SubtractValue(leftJs, rightJs, context),
            BinaryOperator.Multiply => MultiplyValue(leftJs, rightJs, context),
            BinaryOperator.Divide => DivideValue(leftJs, rightJs, context),
            BinaryOperator.Modulo => ModuloValue(leftJs, rightJs, context),
            BinaryOperator.Power => PowerValue(leftJs, rightJs, context),
            BinaryOperator.Equal => LooseEqualsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            BinaryOperator.NotEqual => !LooseEqualsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictEqual => StrictEqualsValue(leftJs, rightJs) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictNotEqual => !StrictEqualsValue(leftJs, rightJs) ? JsValue.True : JsValue.False,
            BinaryOperator.LessThan => LessThanValue(leftJs, rightJs, context),
            BinaryOperator.LessThanOrEqual => LessThanOrEqualValue(leftJs, rightJs, context),
            BinaryOperator.GreaterThan => GreaterThanValue(leftJs, rightJs, context),
            BinaryOperator.GreaterThanOrEqual => GreaterThanOrEqualValue(leftJs, rightJs, context),
            BinaryOperator.BitwiseAnd => BitwiseAndValue(leftJs, rightJs, context),
            BinaryOperator.BitwiseOr => BitwiseOrValue(leftJs, rightJs, context),
            BinaryOperator.BitwiseXor => BitwiseXorValue(leftJs, rightJs, context),
            BinaryOperator.LeftShift => LeftShiftValue(leftJs, rightJs, context),
            BinaryOperator.RightShift => RightShiftValue(leftJs, rightJs, context),
            BinaryOperator.UnsignedRightShift => UnsignedRightShiftValue(leftJs, rightJs, context),
            BinaryOperator.In => InOperatorJsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            BinaryOperator.InstanceOf => InstanceofOperatorJsValue(leftJs, rightJs, context)
                ? JsValue.True
                : JsValue.False,
            _ => throw new NotSupportedException(
                $"Compound assignment operator '{binary.Operator}' is not supported yet.")
        };
        shouldAssign = true;

        return true;
    }

    /// <summary>
    /// Slot-based compound assignment evaluation - fastest path for resolved identifiers.
    /// Only used when ScopeDepth=0 (local variables in the current function scope).
    /// </summary>
    private static bool TryEvaluateCompoundAssignmentSlotBased(
        AssignmentExpression assignment,
        ExpressionNode candidate,
        IdentifierExpression targetIdentifier,
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

        if (!environment.TryReadIdentifierWithSlot(
                targetIdentifier,
                context,
                out var leftJs))
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return false;
        }

        if (context.ShouldStopEvaluation)
        {
            value = leftJs;
            shouldAssign = false;
            return true;
        }

        switch (binary.Operator)
        {
            case BinaryOperator.LogicalAnd:
                if (!leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
            case BinaryOperator.LogicalOr:
                if (leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
            case BinaryOperator.NullishCoalescing:
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
            BinaryOperator.Add => AddValue(leftJs, rightJs, context),
            BinaryOperator.Subtract => SubtractValue(leftJs, rightJs, context),
            BinaryOperator.Multiply => MultiplyValue(leftJs, rightJs, context),
            BinaryOperator.Divide => DivideValue(leftJs, rightJs, context),
            BinaryOperator.Modulo => ModuloValue(leftJs, rightJs, context),
            BinaryOperator.Power => PowerValue(leftJs, rightJs, context),
            BinaryOperator.Equal => LooseEqualsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            BinaryOperator.NotEqual => !LooseEqualsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictEqual => StrictEqualsValue(leftJs, rightJs) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictNotEqual => !StrictEqualsValue(leftJs, rightJs) ? JsValue.True : JsValue.False,
            BinaryOperator.LessThan => LessThanValue(leftJs, rightJs, context),
            BinaryOperator.LessThanOrEqual => LessThanOrEqualValue(leftJs, rightJs, context),
            BinaryOperator.GreaterThan => GreaterThanValue(leftJs, rightJs, context),
            BinaryOperator.GreaterThanOrEqual => GreaterThanOrEqualValue(leftJs, rightJs, context),
            BinaryOperator.BitwiseAnd => BitwiseAndValue(leftJs, rightJs, context),
            BinaryOperator.BitwiseOr => BitwiseOrValue(leftJs, rightJs, context),
            BinaryOperator.BitwiseXor => BitwiseXorValue(leftJs, rightJs, context),
            BinaryOperator.LeftShift => LeftShiftValue(leftJs, rightJs, context),
            BinaryOperator.RightShift => RightShiftValue(leftJs, rightJs, context),
            BinaryOperator.UnsignedRightShift => UnsignedRightShiftValue(leftJs, rightJs, context),
            BinaryOperator.In => InOperatorJsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            BinaryOperator.InstanceOf => InstanceofOperatorJsValue(leftJs, rightJs, context)
                ? JsValue.True
                : JsValue.False,
            _ => throw new NotSupportedException(
                $"Compound assignment operator '{binary.Operator}' is not supported yet.")
        };
        shouldAssign = true;

        return true;
    }

    /// <summary>
    /// Slot-based compound assignment evaluation using cached slot targets.
    /// Avoids resolving the identifier on every iteration.
    /// </summary>
    private static bool TryEvaluateCompoundAssignmentCachedSlot(
        AssignmentExpression assignment,
        ExpressionNode candidate,
        EvaluationContext.CachedSlotTarget cached,
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

        if (!cached.Environment.TryReadSlotValue(cached.Name, cached.SlotIndex, context, out var leftJs))
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return false;
        }

        if (context.ShouldStopEvaluation)
        {
            value = leftJs;
            shouldAssign = false;
            return true;
        }

        switch (binary.Operator)
        {
            case BinaryOperator.LogicalAnd:
                if (!leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
            case BinaryOperator.LogicalOr:
                if (leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
            case BinaryOperator.NullishCoalescing:
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

        value = binary.Operator switch
        {
            BinaryOperator.Add => AddValue(leftJs, rightJs, context),
            BinaryOperator.Subtract => SubtractValue(leftJs, rightJs, context),
            BinaryOperator.Multiply => MultiplyValue(leftJs, rightJs, context),
            BinaryOperator.Divide => DivideValue(leftJs, rightJs, context),
            BinaryOperator.Modulo => ModuloValue(leftJs, rightJs, context),
            BinaryOperator.Power => PowerValue(leftJs, rightJs, context),
            BinaryOperator.Equal => LooseEqualsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            BinaryOperator.NotEqual => !LooseEqualsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictEqual => StrictEqualsValue(leftJs, rightJs) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictNotEqual => !StrictEqualsValue(leftJs, rightJs) ? JsValue.True : JsValue.False,
            BinaryOperator.LessThan => LessThanValue(leftJs, rightJs, context),
            BinaryOperator.LessThanOrEqual => LessThanOrEqualValue(leftJs, rightJs, context),
            BinaryOperator.GreaterThan => GreaterThanValue(leftJs, rightJs, context),
            BinaryOperator.GreaterThanOrEqual => GreaterThanOrEqualValue(leftJs, rightJs, context),
            BinaryOperator.BitwiseAnd => BitwiseAndValue(leftJs, rightJs, context),
            BinaryOperator.BitwiseOr => BitwiseOrValue(leftJs, rightJs, context),
            BinaryOperator.BitwiseXor => BitwiseXorValue(leftJs, rightJs, context),
            BinaryOperator.LeftShift => LeftShiftValue(leftJs, rightJs, context),
            BinaryOperator.RightShift => RightShiftValue(leftJs, rightJs, context),
            BinaryOperator.UnsignedRightShift => UnsignedRightShiftValue(leftJs, rightJs, context),
            BinaryOperator.In => InOperatorJsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            BinaryOperator.InstanceOf => InstanceofOperatorJsValue(leftJs, rightJs, context)
                ? JsValue.True
                : JsValue.False,
            _ => throw new NotSupportedException(
                $"Compound assignment operator '{binary.Operator}' is not supported yet.")
        };
        shouldAssign = true;

        return true;
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
            case BinaryOperator.LogicalAnd:
                if (!leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
            case BinaryOperator.LogicalOr:
                if (leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return true;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return true;
            case BinaryOperator.NullishCoalescing:
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
            BinaryOperator.Add => AddValue(leftJs, rightJs, context),
            BinaryOperator.Subtract => SubtractValue(leftJs, rightJs, context),
            BinaryOperator.Multiply => MultiplyValue(leftJs, rightJs, context),
            BinaryOperator.Divide => DivideValue(leftJs, rightJs, context),
            BinaryOperator.Modulo => ModuloValue(leftJs, rightJs, context),
            BinaryOperator.Power => PowerValue(leftJs, rightJs, context),
            BinaryOperator.Equal => LooseEqualsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            BinaryOperator.NotEqual => !LooseEqualsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictEqual => StrictEqualsValue(leftJs, rightJs) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictNotEqual => !StrictEqualsValue(leftJs, rightJs) ? JsValue.True : JsValue.False,
            BinaryOperator.LessThan => LessThanValue(leftJs, rightJs, context),
            BinaryOperator.LessThanOrEqual => LessThanOrEqualValue(leftJs, rightJs, context),
            BinaryOperator.GreaterThan => GreaterThanValue(leftJs, rightJs, context),
            BinaryOperator.GreaterThanOrEqual => GreaterThanOrEqualValue(leftJs, rightJs, context),
            BinaryOperator.BitwiseAnd => BitwiseAndValue(leftJs, rightJs, context),
            BinaryOperator.BitwiseOr => BitwiseOrValue(leftJs, rightJs, context),
            BinaryOperator.BitwiseXor => BitwiseXorValue(leftJs, rightJs, context),
            BinaryOperator.LeftShift => LeftShiftValue(leftJs, rightJs, context),
            BinaryOperator.RightShift => RightShiftValue(leftJs, rightJs, context),
            BinaryOperator.UnsignedRightShift => UnsignedRightShiftValue(leftJs, rightJs, context),
            BinaryOperator.In => InOperatorJsValue(leftJs, rightJs, context) ? JsValue.True : JsValue.False,
            BinaryOperator.InstanceOf => InstanceofOperatorJsValue(leftJs, rightJs, context)
                ? JsValue.True
                : JsValue.False,
            _ => throw new NotSupportedException(
                $"Compound assignment operator '{binary.Operator}' is not supported yet.")
        };
        shouldAssign = true;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue EvaluateAssignmentRhsWithNameHintJsValue(
        AssignmentExpression? assignment,
        ExpressionNode rhs,
        JsEnvironment environment,
        EvaluationContext context)
    {
        using var functionNameHint = ShouldApplyAssignmentNameHint(assignment, rhs)
            ? context.EnterFunctionNameHint(assignment!.Target)
            : null;

        var jsValue = rhs.EvaluateExpression(environment, context);
        if (context.ShouldStopEvaluation)
        {
            return jsValue;
        }

        if (assignment is not null &&
            jsValue.ObjectValue is IFunctionNameTarget nameTarget &&
            ExpressionNode.IsAnonymousFunctionDefinitionNode(rhs) &&
            !IsParenthesizedIdentifierAssignment(assignment))
        {
            nameTarget.EnsureHasName(assignment.Target.Name);
        }

        return jsValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldApplyAssignmentNameHint(AssignmentExpression? assignment, ExpressionNode rhs)
    {
        return assignment is not null && ExpressionNode.IsAnonymousFunctionDefinitionNode(rhs) &&
               !IsParenthesizedIdentifierAssignment(assignment);
    }

    extension(AssignmentExpression expression)
    {
        private JsValue EvaluateAssignment(JsEnvironment environment,
            EvaluationContext context)
        {
            // Check for immutable binding (e.g., named function expression name)
            // Per ECMAScript spec, in strict mode throw TypeError, in non-strict mode silently ignore
            if (expression.IsImmutableTarget)
            {
                // Still need to evaluate RHS for potential side effects
                var rhsValue = expression.Value.EvaluateExpression(environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return rhsValue;
                }

                // Find the binding's environment to check its strictness
                // The binding's environment strictness determines the behavior, not the current execution context
                var bindingEnv = expression.ScopeId >= 0 && environment.ScopeId == expression.ScopeId
                    ? environment
                    : expression.ScopeId >= 0
                        ? environment.FindByScopeId(expression.ScopeId)
                        : environment.GetFunctionScope();
                var isStrictBinding = bindingEnv?.IsStrict ?? environment.IsStrict;

                if (isStrictBinding)
                {
                    var error = StandardLibrary.CreateTypeError(
                        $"Assignment to constant variable '{expression.Target.Name}'.", context, context.RealmState);
                    context.SetThrow(error);
                    return JsValue.Undefined;
                }

                // Non-strict mode: silently ignore the assignment, return the evaluated value
                return rhsValue;
            }

            // Fast path: slot-based assignment using ScopeId to find the declaring environment.
            // This enables O(1) slot access for variables in any scope (local or closure).
            if ( expression is { SlotIndex: >= 0, ScopeId: >= 0 })
            {
                var targetIdentifier = expression.TargetIdentifier ??
                                       new IdentifierExpression(
                                           expression.Source,
                                           expression.Target,
                                           expression.ScopeDepth,
                                           expression.SlotIndex,
                                           expression.ScopeId);

                if (expression.IsCompoundAssignment &&
                    TryEvaluateCompoundAssignmentSlotBased(
                        expression,
                        expression.Value,
                        targetIdentifier,
                        environment,
                        context,
                        out var compoundJsValue,
                        out var shouldAssignCompound))
                {
                    if (context.ShouldStopEvaluation)
                    {
                        return compoundJsValue;
                    }

                    if (shouldAssignCompound)
                    {
                        environment.TryWriteIdentifierWithSlot(targetIdentifier, compoundJsValue, context);
                    }

                    return compoundJsValue;
                }

                // Simple slot-based assignment (not compound)
                var slotValueJs =
                    EvaluateAssignmentRhsWithNameHintJsValue(expression, expression.Value, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return slotValueJs;
                }

                environment.TryWriteIdentifierWithSlot(targetIdentifier, slotValueJs, context);
                return slotValueJs;
            }

            // Fast path for compound assignments on simple identifiers
            // This avoids creating AssignmentReference structs entirely.
            // IMPORTANT: Only use this fast path for non-dynamic scopes (see comment below for simple assignments).
            if (
                expression is { IsCompoundAssignment: true, SlotIndex: >= 0, ScopeId: >= 0 } &&
                TryEvaluateCompoundAssignmentDirectJsValue(expression, expression.Value, expression.Target,
                    environment, context, out var compoundJsValue2, out var shouldAssignCompound2))
            {
                if (context.ShouldStopEvaluation)
                {
                    return compoundJsValue2;
                }

                if (shouldAssignCompound2)
                {
                    environment.SetIdentifierJsValue(expression.Target, compoundJsValue2, context);
                }

                return compoundJsValue2;
            }

            // Fast path for simple identifier assignments (not compound)
            // This avoids creating AssignmentReference structs entirely.
            // IMPORTANT: Only use this fast path for non-dynamic scopes!
            // Dynamic scopes (with eval/with) require resolving the reference BEFORE
            // evaluating the RHS, per ES spec 13.15.2. The fast path evaluates RHS first
            // which breaks code like: with(scope) { x = (delete scope.x, 2); }
            if ( expression is { IsCompoundAssignment: false, SlotIndex: >= 0, ScopeId: >= 0 })
            {
                var targetValueJs =
                    EvaluateAssignmentRhsWithNameHintJsValue(expression, expression.Value, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return targetValueJs;
                }

                try
                {
                    environment.SetIdentifierJsValue(expression.Target, targetValueJs, context);
                    return targetValueJs;
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                               StringComparison.Ordinal))
                {
                    return AssignmentExpression.HandleReferenceError(ex, environment, context);
                }
            }

            // Runtime slot lookup: try to find slot index from environment's SlotMap
            // This avoids the expensive ResolveIdentifierDirect fallback for variables
            // declared in the current function scope when AST nodes weren't pre-stamped.
            if (environment.TryGetSlotIndex(expression.Target, out var runtimeSlotIndex))
            {
                // Found slot - use direct slot-based assignment
                if (expression.IsCompoundAssignment && expression.Value is BinaryExpression binary)
                {
                    // Read current value from slot
                    var currentValue = environment.GetSlotRef(runtimeSlotIndex);

                    // Short-circuit for logical operators
                    switch (binary.Operator)
                    {
                        case BinaryOperator.LogicalAnd:
                            if (!currentValue.IsTruthy)
                            {
                                return currentValue; // No assignment needed
                            }
                            break;
                        case BinaryOperator.LogicalOr:
                            if (currentValue.IsTruthy)
                            {
                                return currentValue; // No assignment needed
                            }
                            break;
                        case BinaryOperator.NullishCoalescing:
                            if (!currentValue.IsNullish)
                            {
                                return currentValue; // No assignment needed
                            }
                            break;
                    }

                    var rhsValue = binary.Right.EvaluateExpression(environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return rhsValue;
                    }

                    // Apply compound operation
                    var compoundResult = binary.Operator switch
                    {
                        BinaryOperator.Add => AddValue(currentValue, rhsValue, context),
                        BinaryOperator.Subtract => SubtractValue(currentValue, rhsValue, context),
                        BinaryOperator.Multiply => MultiplyValue(currentValue, rhsValue, context),
                        BinaryOperator.Divide => DivideValue(currentValue, rhsValue, context),
                        BinaryOperator.Modulo => ModuloValue(currentValue, rhsValue, context),
                        BinaryOperator.Power => PowerValue(currentValue, rhsValue, context),
                        BinaryOperator.BitwiseAnd => BitwiseAndValue(currentValue, rhsValue, context),
                        BinaryOperator.BitwiseOr => BitwiseOrValue(currentValue, rhsValue, context),
                        BinaryOperator.BitwiseXor => BitwiseXorValue(currentValue, rhsValue, context),
                        BinaryOperator.LeftShift => LeftShiftValue(currentValue, rhsValue, context),
                        BinaryOperator.RightShift => RightShiftValue(currentValue, rhsValue, context),
                        BinaryOperator.UnsignedRightShift => UnsignedRightShiftValue(currentValue, rhsValue, context),
                        // For logical operators, the rhs value becomes the new value
                        BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr or BinaryOperator.NullishCoalescing => rhsValue,
                        _ => throw new NotSupportedException($"Compound assignment operator '{binary.Operator}' is not supported.")
                    };

                    environment.SetSlot(runtimeSlotIndex, compoundResult);
                    return compoundResult;
                }
                else
                {
                    // Simple assignment
                    var rhsValue = EvaluateAssignmentRhsWithNameHintJsValue(expression, expression.Value, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return rhsValue;
                    }

                    environment.SetSlot(runtimeSlotIndex, rhsValue);
                    return rhsValue;
                }
            }

            if (context.TryResolveAssignmentSlot(expression, environment, out var cachedSlot))
            {
                if (expression.IsCompoundAssignment &&
                    TryEvaluateCompoundAssignmentCachedSlot(expression, expression.Value, cachedSlot, environment, context,
                        out var cachedCompoundValue,
                        out var cachedShouldAssign))
                {
                    if (context.ShouldStopEvaluation)
                    {
                        return cachedCompoundValue;
                    }

                    if (cachedShouldAssign)
                    {
                        if (!cachedSlot.Environment.TryWriteSlotValue(
                                cachedSlot.Name,
                                cachedSlot.SlotIndex,
                                cachedCompoundValue,
                                context))
                        {
                            environment.SetIdentifierJsValue(cachedSlot.Name, cachedCompoundValue, context);
                        }
                    }

                    return cachedCompoundValue;
                }

                var cachedValue =
                    EvaluateAssignmentRhsWithNameHintJsValue(expression, expression.Value, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return cachedValue;
                }

                if (!cachedSlot.Environment.TryWriteSlotValue(cachedSlot.Name, cachedSlot.SlotIndex, cachedValue, context))
                {
                    environment.SetIdentifierJsValue(cachedSlot.Name, cachedValue, context);
                }

                return cachedValue;
            }

            // Fallback to the AssignmentReference path for other cases
            var reference = AssignmentReferenceResolver.ResolveIdentifierDirect(
                expression.Target, environment, context);

            // Use JsValue version of the compound assignment to avoid boxing
            if (expression.IsCompoundAssignment &&
                TryEvaluateCompoundAssignmentJsValue(expression, expression.Value, reference, environment, context,
                    out var refCompoundJsValue,
                    out var refShouldAssignCompound))
            {
                if (context.ShouldStopEvaluation)
                {
                    return refCompoundJsValue;
                }

                if (refShouldAssignCompound)
                {
                    reference.SetValue(refCompoundJsValue);
                }

                return refCompoundJsValue;
            }

            // Use JsValue version to avoid boxing
            var valueJs = EvaluateAssignmentRhsWithNameHintJsValue(expression, expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return valueJs;
            }

            try
            {
                reference.SetValue(valueJs);
                return valueJs;
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                           StringComparison.Ordinal))
            {
                return AssignmentExpression.HandleReferenceError(ex, environment, context);
            }
        }

        private static JsValue HandleReferenceError(InvalidOperationException ex, JsEnvironment environment,
            EvaluationContext context)
        {
            var errorValue = (JsValue)ex.Message;

            // If a ReferenceError constructor is available, use it to
            // create a proper JS error instance so user code can catch
            // and inspect it.
            if (environment.TryGetObject<IJsCallable>(Symbol.ReferenceErrorIdentifier, out var callable))
            {
                errorValue = callable.Invoke([(JsValue)ex.Message], JsValue.Undefined);
            }

            context.SetThrow(errorValue);
            return errorValue;
        }
    }
}
