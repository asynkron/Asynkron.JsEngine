#region

using System.Runtime.CompilerServices;
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
    /// Core logic for evaluating compound assignment operators.
    /// Handles short-circuit evaluation for logical operators and applies binary operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EvaluateCompoundOperator(
        AssignmentExpression? assignment,
        BinaryExpression binary,
        JsValue leftJs,
        JsEnvironment environment,
        EvaluationContext context,
        out JsValue value,
        out bool shouldAssign)
    {
        switch (binary.Operator)
        {
            case BinaryOperator.LogicalAnd:
                if (!leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return;
            case BinaryOperator.LogicalOr:
                if (leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return;
            case BinaryOperator.NullishCoalescing:
                if (!leftJs.IsNullish)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return;
        }

        var rightJs = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
        if (context.ShouldStopEvaluation)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return;
        }

        value = ApplyBinaryOperator(binary.Operator, leftJs, rightJs, context);
        shouldAssign = true;
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

        var leftJs = context.GetIdentifier(environment, target);
        if (context.ShouldStopEvaluation)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return true;
        }

        EvaluateCompoundOperator(assignment, binary, leftJs, environment, context, out value, out shouldAssign);
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

        EvaluateCompoundOperator(assignment, binary, leftJs, environment, context, out value, out shouldAssign);
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

        EvaluateCompoundOperator(assignment, binary, leftJs, environment, context, out value, out shouldAssign);
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

        var leftJs = reference.GetJsValue();
        if (context.ShouldStopEvaluation)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return true;
        }

        EvaluateCompoundOperator(assignment, binary, leftJs, environment, context, out value, out shouldAssign);
        return true;
    }

    /// <summary>
    /// Tries to evaluate a compound assignment and apply it to the reference.
    /// Combines TryEvaluateCompoundAssignmentJsValue with the common post-processing pattern.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryApplyCompoundAssignment(
        AssignmentExpression? assignment,
        ExpressionNode candidate,
        AssignmentReference reference,
        JsEnvironment environment,
        EvaluationContext context,
        out JsValue result)
    {
        if (!TryEvaluateCompoundAssignmentJsValue(assignment, candidate, reference, environment, context,
                out var compoundValue, out var shouldAssign))
        {
            result = JsValue.Undefined;
            return false;
        }

        if (!context.ShouldStopEvaluation && shouldAssign)
        {
            reference.SetValue(compoundValue);
        }

        result = compoundValue;
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
            var target = expression.Target;

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
                var immutableBindingEnv = expression.ScopeId >= 0 && environment.ScopeId == expression.ScopeId
                    ? environment
                    : expression.ScopeId >= 0
                        ? environment.FindByScopeId(expression.ScopeId)
                        : environment.GetFunctionScope();
                var isStrictBinding = immutableBindingEnv?.IsStrict ?? environment.IsStrict;

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
            if (expression is { SlotIndex: >= 0, ScopeId: >= 0 })
            {
                var targetIdentifier = expression.TargetIdentifier ??
                                       new IdentifierExpression(
                                           expression.Source,
                                           expression.Target,
                                           expression.ScopeDepth,
                                           expression.SlotIndex,
                                           expression.ScopeId);

                if (expression.IsCompoundAssignment)
                {
                    // Try slot-based compound assignment (fastest path)
                    if (TryEvaluateCompoundAssignmentSlotBased(
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
                    // If slot-based compound fails, fall through to other compound handlers below
                }
                else
                {
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
            }

            // Pre-check for const/immutable bindings using scope analysis (before fast paths that may bypass flags)
            // Only applies to simple identifier targets.
            if (environment.TryFindBindingJsValue(target, allowUninitialized: true,
                    out var precheckBindingEnv, out _))
            {
                if (precheckBindingEnv.TryGetSlotIndex(target, out var idx))
                {
                    ref var slot = ref precheckBindingEnv.GetSlotByIndex(idx);
                    if (!slot.IsUninitialized)
                    {
                        var isStrictContext = precheckBindingEnv.IsStrict || context.CurrentScope.IsStrict;
                        if (slot.IsConst ||
                            (slot.IsImmutableBinding && isStrictContext))
                        {
                            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                                $"Assignment to constant variable '{target.Name}'.",
                                realm: context.RealmState));
                        }
                    }
                }
            }

            // Fast path for compound assignments on simple identifiers
            // This avoids creating AssignmentReference structs entirely.
            // IMPORTANT: Only use this fast path for non-dynamic scopes (see comment below for simple assignments).
            if (expression is { IsCompoundAssignment: true, SlotIndex: >= 0, ScopeId: >= 0 } &&
                TryEvaluateCompoundAssignmentDirectJsValue(expression, expression.Value, target,
                    environment, context, out var compoundJsValue2, out var shouldAssignCompound2))
            {
                if (context.ShouldStopEvaluation)
                {
                    return compoundJsValue2;
                }

                if (shouldAssignCompound2)
                {
                    var slotEnvironment = environment.ScopeId == expression.ScopeId
                        ? environment
                        : environment.FindByScopeId(expression.ScopeId) ?? environment;
                    slotEnvironment.SetIdentifierJsValue(target, compoundJsValue2, context);
                }

                return compoundJsValue2;
            }

            // Fast path for simple identifier assignments (not compound)
            // This avoids creating AssignmentReference structs entirely.
            // IMPORTANT: Only use this fast path for non-dynamic scopes!
            // Dynamic scopes (with eval/with) require resolving the reference BEFORE
            // evaluating the RHS, per ES spec 13.15.2. The fast path evaluates RHS first
            // which breaks code like: with(scope) { x = (delete scope.x, 2); }
            if (expression is { IsCompoundAssignment: false, SlotIndex: >= 0, ScopeId: >= 0 })
            {
                // Find the environment that owns this slot. Slot indices are scoped to the declaring environment,
                // so we must not blindly write to the current environment if ScopeId differs (e.g., class name slots).
                var slotEnvironment = environment.ScopeId == expression.ScopeId
                    ? environment
                    : environment.FindByScopeId(expression.ScopeId) ?? environment;

                var targetValueJs =
                    EvaluateAssignmentRhsWithNameHintJsValue(expression, expression.Value, slotEnvironment, context);
                if (context.ShouldStopEvaluation)
                {
                    return targetValueJs;
                }

                try
                {
                    slotEnvironment.SetIdentifierJsValue(target, targetValueJs, context);
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
            if (environment.TryGetSlotIndex(target, out var runtimeSlotIndex))
            {
                // Found slot - check TDZ first
                ref var tdzdCheckSlot = ref environment.GetSlotByIndex(runtimeSlotIndex);
                if (tdzdCheckSlot.IsUninitialized && tdzdCheckSlot.IsLexical)
                {
                    throw new ThrowSignal(StandardLibrary.CreateReferenceError(
                        $"Cannot access '{target.Name}' before initialization",
                        context, context.RealmState));
                }

                if (expression.IsCompoundAssignment && expression.Value is BinaryExpression binary)
                {
                    // Read current value from slot (already TDZ-checked above)
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

                    var rhsValue = EvaluateAssignmentRhsWithNameHintJsValue(
                        expression,
                        binary.Right,
                        environment,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        return rhsValue;
                    }

                    // Apply compound operation using shared helper
                    var compoundResult = ApplyBinaryOperator(binary.Operator, currentValue, rhsValue, context);

                    // Check const before assignment - const always throws per ES spec
                    ref var compoundSlot = ref environment.GetSlotByIndex(runtimeSlotIndex);
                    if (compoundSlot.IsConst && !compoundSlot.IsUninitialized)
                    {
                        throw new ThrowSignal(StandardLibrary.CreateTypeError(
                            $"Assignment to constant variable '{target.Name}'.",
                            realm: context.RealmState));
                    }

                    if (compoundSlot.IsImmutableBinding)
                    {
                        if (environment.IsStrict || context.CurrentScope.IsStrict)
                        {
                            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                                $"Assignment to constant variable '{target.Name}'.",
                                realm: context.RealmState));
                        }

                        // Sloppy mode: ignore write but still return the computed value
                        return compoundResult;
                    }

                    compoundSlot.Value = compoundResult;

                    // For non-lexical bindings (var) in global scope, also update the global object
                    if (!compoundSlot.IsLexical && environment.IsGlobalFunctionScope)
                    {
                        var globalObject = environment.GetRootGlobalObject();
                        globalObject?.SetProperty(expression.Target.Name, compoundResult);
                    }

                    return compoundResult;
                }
                else
                {
                    // Simple assignment
                    var rhsValue =
                        EvaluateAssignmentRhsWithNameHintJsValue(expression, expression.Value, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return rhsValue;
                    }

                    // Check const before assignment - const always throws per ES spec
                    ref var simpleSlot = ref environment.GetSlotByIndex(runtimeSlotIndex);
                    if (simpleSlot.IsConst && !simpleSlot.IsUninitialized)
                    {
                        throw new ThrowSignal(StandardLibrary.CreateTypeError(
                            $"Assignment to constant variable '{target.Name}'.",
                            realm: context.RealmState));
                    }

                    if (simpleSlot.IsImmutableBinding)
                    {
                        if (environment.IsStrict || context.CurrentScope.IsStrict)
                        {
                            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                                $"Assignment to constant variable '{target.Name}'.",
                                realm: context.RealmState));
                        }

                        return rhsValue;
                    }

                    simpleSlot.Value = rhsValue;

                    // For non-lexical bindings (var) in global scope, also update the global object
                    if (!simpleSlot.IsLexical && environment.IsGlobalFunctionScope)
                    {
                        var globalObject = environment.GetRootGlobalObject();
                        globalObject?.SetProperty(expression.Target.Name, rhsValue);
                    }

                    return rhsValue;
                }
            }

            if (context.TryResolveAssignmentSlot(expression, environment, out var cachedSlot))
            {
                if (expression.IsCompoundAssignment &&
                    TryEvaluateCompoundAssignmentCachedSlot(expression, expression.Value, cachedSlot, environment,
                        context,
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

                if (!cachedSlot.Environment.TryWriteSlotValue(cachedSlot.Name, cachedSlot.SlotIndex, cachedValue,
                        context))
                {
                    environment.SetIdentifierJsValue(cachedSlot.Name, cachedValue, context);
                }

                return cachedValue;
            }

            // Fallback to the AssignmentReference path for other cases
            var reference = AssignmentReferenceResolver.ResolveIdentifierDirect(
                expression.Target, environment, context);

            // Spec: GetValue on an unresolvable Reference (compound assignments must read first)
            // must throw ReferenceError in both strict and non-strict mode.
            if (expression.IsCompoundAssignment && reference.IsUnresolvable)
            {
                throw StandardLibrary.ThrowReferenceError(
                    $"{expression.Target.Name} is not defined",
                    context,
                    context.RealmState);
            }

            // Use JsValue version of the compound assignment to avoid boxing
            if (expression.IsCompoundAssignment &&
                TryApplyCompoundAssignment(expression, expression.Value, reference, environment, context, out var refCompoundJsValue))
            {
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
                errorValue = callable.Invoke(new SingleValueArgs((JsValue)ex.Message), JsValue.Undefined);
            }

            context.SetThrow(errorValue);
            return errorValue;
        }
    }
}
