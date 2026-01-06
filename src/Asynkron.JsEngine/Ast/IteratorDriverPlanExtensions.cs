#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Rents an iteration environment from the pool with slots initialized.
    /// Avoids Func&lt;JsEnvironment&gt; lambda allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsEnvironment RentIterationEnvironment(this IteratorDriverPlan plan, JsEnvironment loopEnvironment, ILogger? logger = null)
    {
        var env = JsEnvironmentPool.Rent(loopEnvironment, false, false, plan.Body.Source, "for-each-iteration",
            logger: logger);
        InitializeIterationEnvironmentLayout(plan, env);
        return env;
    }

    /// <summary>
    /// Selects or creates the appropriate iteration environment for the current iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsEnvironment SelectIterationEnvironment(this IteratorDriverPlan plan, JsEnvironment? reusableIterationEnvironment,
        JsEnvironment loopEnvironment,
        bool useIterationSlots,
        ILogger? logger)
    {
        if (reusableIterationEnvironment is not null)
        {
            return reusableIterationEnvironment;
        }

        if (plan.DeclarationKind is not (VariableKind.Let or VariableKind.Const
            or VariableKind.Using or VariableKind.AwaitUsing))
        {
            return loopEnvironment;
        }

        return useIterationSlots
            ? plan.RentIterationEnvironment(loopEnvironment, logger)
            : new JsEnvironment(loopEnvironment,
                creatingSource: plan.Body.Source,
                description: "for-each-iteration");
    }

    /// <summary>
    /// Handles the first iteration assignment for let/const bindings with reusable environment.
    /// Sets up the const flag and syncs iteration slots.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleFirstIterationAssignment(this IteratorDriverPlan plan, JsValue value,
        JsEnvironment reusableIterationEnvironment,
        JsEnvironment outerEnvironment,
        EvaluationContext context,
        ref bool letConstFirstIterationDone)
    {
        plan.Target.AssignLoopBinding(value, reusableIterationEnvironment, outerEnvironment, context,
            plan.DeclarationKind);
        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        plan.SyncIterationSlots(reusableIterationEnvironment, context);
        letConstFirstIterationDone = true;
    }

    private static JsValue ExecuteIteratorDriverJsValue(this IteratorDriverPlan plan, IJsObjectLike? iterator,
        IEnumerator<JsValue>? enumerator,
        JsEnvironment loopEnvironment,
        JsEnvironment outerEnvironment,
        EvaluationContext context,
        Symbol? loopLabel,
        bool useIterationSlots = false)
    {
        var lastValueJs = JsValue.Undefined;
        var iteratorDone = false;

        var state = IteratorDriverStatePool.Rent();
        state.IteratorObject = iterator;
        state.Enumerator = enumerator;
        state.IsAsyncIterator = plan.Kind == IteratorDriverKind.Await;
        state.NextMethod = iterator?.GetIteratorNextCallable(context);

        // OPTIMIZATION: Check if we can use the fast slot path for simple identifier bindings
        // This avoids dictionary-based AssignLoopBinding and SyncIterationSlots per iteration
        var canUseSlotFastPath = plan.Target is IdentifierBinding &&
                                 !plan.PerIterationSlotIndices.IsDefaultOrEmpty &&
                                 plan.PerIterationSlotIndices.Length == 1 &&
                                 plan.PerIterationSlotIndices[0] >= 0 &&
                                 useIterationSlots;
        var fastPathSlotIndex = canUseSlotFastPath ? plan.PerIterationSlotIndices[0] : -1;

        // OPTIMIZATION: For 'var' bindings, pre-resolve JsVariable once and reuse for all iterations
        // This gives O(1) direct slot access via JsVariable.Write() instead of SetSlot() per iteration
        var canCacheVarVariable = canUseSlotFastPath && plan.DeclarationKind == VariableKind.Var;
        var cachedVarVariable = canCacheVarVariable
            ? new JsVariable(loopEnvironment, fastPathSlotIndex)
            : default;

        // OPTIMIZATION: For let/const bindings when no closures exist in loop body,
        // we can reuse the same iteration environment for all iterations.
        // This allows JsVariable caching similar to 'var' bindings.
        JsEnvironment? reusableIterationEnvironment = null;
        var cachedLetConstVariable = default(JsVariable);
        var canReuseLetConstEnv = canUseSlotFastPath &&
                                  plan.DeclarationKind is VariableKind.Let or VariableKind.Const &&
                                  plan.CanReuseIterationEnvironment;
        var letConstFirstIterationDone = false; // Track if we've done the first iteration setup
        var logger = context.RealmState.Logger;
        if (canReuseLetConstEnv)
        {
            // Create ONE iteration environment before the loop and cache JsVariable
            reusableIterationEnvironment = useIterationSlots
                ? plan.RentIterationEnvironment(loopEnvironment, logger)
                : new JsEnvironment(loopEnvironment, creatingSource: plan.Body.Source,
                    description: "for-each-iteration-reused");
            cachedLetConstVariable = new JsVariable(reusableIterationEnvironment, fastPathSlotIndex);
        }

        // FAST PATH: Try tight loop for simple accumulator patterns like: for (const n of arr) { sum += n; }
        // This avoids calling EvaluateStatementJsValue per iteration
        // Only for sync for-of (not for await...of which needs async handling)
        if (plan.Kind != IteratorDriverKind.Await &&
            enumerator is not null &&
            loopLabel is null &&
            (cachedLetConstVariable.IsValid || cachedVarVariable.IsValid) &&
            plan.TryExecuteFastForOfAccumulator(enumerator, loopEnvironment,
                reusableIterationEnvironment ?? loopEnvironment,
                fastPathSlotIndex, out lastValueJs))
        {
            // Return pooled resources before early return
            if (reusableIterationEnvironment is not null)
            {
                JsEnvironmentPool.Return(reusableIterationEnvironment);
            }

            IteratorDriverStatePool.Return(state);
            return lastValueJs;
        }

        try
        {
            while (!context.ShouldStopEvaluation)
            {
                context.ThrowIfCancellationRequested();

                object? nextResult = null;
                if (state.IteratorObject is not null)
                {
                    var throwBeforeNext = context.IsThrow;
                    nextResult = state.IteratorObject.InvokeIteratorNext(
                        state.NextMethod!,
                        context: context,
                        callingEnvironment: loopEnvironment);
                    if (!throwBeforeNext && context.IsThrow)
                    {
                        // Per ES spec 13.6.4.13 step 5.d: if IteratorStep (calling next())
                        // returns an abrupt completion, we return that completion directly
                        // WITHOUT calling IteratorClose.
                        var thrown = context.FlowValue;
                        context.Clear();
                        throw new ThrowSignal(thrown);
                    }
                }
                else if (state.Enumerator is not null)
                {
                    if (!state.Enumerator.MoveNext())
                    {
                        break;
                    }

                    // FAST PATH: Enumerator yields JsValue directly - skip iterator result unwrapping
                    // and go directly to body execution. This avoids creating {done, value} objects.
                    var enumeratorValue = state.Enumerator.Current;

                    var iterationEnvironment = plan.SelectIterationEnvironment(
                        reusableIterationEnvironment, loopEnvironment, useIterationSlots, logger);

                    // OPTIMIZATION: For simple identifier bindings, write directly to slot
                    // This avoids dictionary-based AssignLoopBinding and SyncIterationSlots
                    if (cachedVarVariable.IsValid)
                    {
                        // Fastest path: pre-resolved JsVariable for 'var' bindings
                        cachedVarVariable.Write(enumeratorValue);
                    }
                    else if (cachedLetConstVariable.IsValid && letConstFirstIterationDone)
                    {
                        // Fast path: pre-resolved JsVariable for let/const when no closures
                        // Only use after first iteration (which sets up the const binding properly)
                        cachedLetConstVariable.Write(enumeratorValue);
                    }
                    else if (cachedLetConstVariable.IsValid && !letConstFirstIterationDone)
                    {
                        plan.HandleFirstIterationAssignment(enumeratorValue, reusableIterationEnvironment!,
                            outerEnvironment, context, ref letConstFirstIterationDone);
                    }
                    else if (canUseSlotFastPath && iterationEnvironment.HasSlots)
                    {
                        // Fast path: direct slot write for let/const bindings
                        iterationEnvironment.SetSlotDirect(fastPathSlotIndex, enumeratorValue);
                    }
                    else
                    {
                        plan.Target.AssignLoopBinding(enumeratorValue, iterationEnvironment, outerEnvironment,
                            context,
                            plan.DeclarationKind);
                        if (context.IsThrow)
                        {
                            throw new ThrowSignal(context.FlowValue);
                        }

                        plan.SyncIterationSlots(iterationEnvironment, context);
                    }

                    // Check if yield/await happened during binding (e.g., yield in destructuring default)
                    if (context.ShouldStopEvaluation)
                    {
                        break;
                    }

                    var bodyResult = plan.Body.EvaluateStatementJsValue(iterationEnvironment, context, loopLabel);
                    if (!bodyResult.IsUnit)
                    {
                        lastValueJs = bodyResult;
                    }

                    if (context.IsThrow)
                    {
                        throw new ThrowSignal(context.FlowValue);
                    }

                    if (context.IsReturn || context.IsThrow)
                    {
                        break;
                    }

                    if (context.TryClearContinue(loopLabel))
                    {
                        continue;
                    }

                    if (context.TryClearBreak(loopLabel))
                    {
                        break;
                    }

                    continue; // Skip the iterator protocol handling below
                }

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();

                    if (state.IteratorObject is not null && !iteratorDone)
                    {
                        state.IteratorObject.IteratorClose(context, true,
                            thrown);

                        if (context.IsThrow)
                        {
                            thrown = context.FlowValue;
                            context.Clear();
                        }
                    }

                    throw new ThrowSignal(thrown);
                }

                // Unwrap JsValue struct if present (only for iterator protocol path)
                if (nextResult is JsValue jsVal)
                {
                    nextResult = jsVal.Kind == JsValueKind.Object ? jsVal.ObjectValue : null;
                }

                if (nextResult is IJsObjectLike resultObj)
                {
                    var done = resultObj.TryGetProperty("done", out var doneValue) &&
                               JsOps.ToBoolean(doneValue);
                    if (done)
                    {
                        iteratorDone = true;
                        break;
                    }

                    JsValue value;
                    // TryGetProperty returns JsValue, keep as JsValue to avoid boxing
                    var gotValue = resultObj.TryGetProperty("value", out var yielded);
                    value = gotValue ? yielded : JsValue.Undefined;

                    var iterationEnvironment = plan.SelectIterationEnvironment(
                        reusableIterationEnvironment, loopEnvironment, useIterationSlots, logger);

                    try
                    {
                        // OPTIMIZATION: For simple identifier bindings, write directly to slot
                        if (cachedVarVariable.IsValid)
                        {
                            // Fastest path: pre-resolved JsVariable for 'var' bindings
                            cachedVarVariable.Write(value);
                        }
                        else if (cachedLetConstVariable.IsValid && letConstFirstIterationDone)
                        {
                            // Fast path: pre-resolved JsVariable for let/const when no closures
                            // Only use after first iteration (which sets up the const binding properly)
                            cachedLetConstVariable.Write(value);
                        }
                        else if (cachedLetConstVariable.IsValid && !letConstFirstIterationDone)
                        {
                            plan.HandleFirstIterationAssignment(value, reusableIterationEnvironment!,
                                outerEnvironment, context, ref letConstFirstIterationDone);
                        }
                        else if (canUseSlotFastPath && iterationEnvironment.HasSlots)
                        {
                            // Fast path: direct slot write for let/const bindings
                            iterationEnvironment.SetSlotDirect(fastPathSlotIndex, value);
                        }
                        else
                        {
                            plan.Target.AssignLoopBinding(value, iterationEnvironment, outerEnvironment, context,
                                plan.DeclarationKind);
                            if (context.IsThrow)
                            {
                                throw new ThrowSignal(context.FlowValue);
                            }

                            plan.SyncIterationSlots(iterationEnvironment, context);
                        }

                        // Check if yield/await happened during binding (e.g., yield in destructuring default)
                        if (context.ShouldStopEvaluation)
                        {
                            break;
                        }

                        // Per ES spec 14.7.5.7 ForIn/OfBodyEvaluation step 5.k-l:
                        // Only update V (completion value) if result.[[Value]] is not empty
                        var bodyResult =
                            plan.Body.EvaluateStatementJsValue(iterationEnvironment, context, loopLabel);
                        if (!bodyResult.IsUnit)
                        {
                            lastValueJs = bodyResult;
                        }

                        if (context.IsThrow)
                        {
                            throw new ThrowSignal(context.FlowValue);
                        }
                    }
                    catch (ThrowSignal)
                    {
                        if (state.IteratorObject is not null && !iteratorDone)
                        {
                            state.IteratorObject.IteratorClose(context, true);
                        }

                        throw;
                    }
                }
                else
                {
                    if (state.IteratorObject is not null)
                    {
                        var typeError = StandardLibrary.CreateTypeError(
                            "Iterator.next() did not return an object", context, context.RealmState);
                        context.RealmState.Logger?.LogInformation(
                            "Iterator.next non-object result; throwing TypeError (label={Label})",
                            loopLabel?.Name ?? "<none>");
                        context.SetThrow(typeError);
                        iteratorDone =
                            false; // force IteratorClose on exit for abrupt completion paths that require it
                        throw new ThrowSignal(typeError);
                    }

                    // Enumerator path (non-object next)
                    var iterationEnvironment = plan.SelectIterationEnvironment(
                        reusableIterationEnvironment, loopEnvironment, useIterationSlots, logger);

                    // OPTIMIZATION: For simple identifier bindings, write directly to slot
                    var nextJsValue = JsValue.FromObjectUnsafe(nextResult);
                    if (cachedVarVariable.IsValid)
                    {
                        // Fastest path: pre-resolved JsVariable for 'var' bindings
                        cachedVarVariable.Write(nextJsValue);
                    }
                    else
                    {
                        switch (cachedLetConstVariable.IsValid)
                        {
                            case true when letConstFirstIterationDone:
                                // Fast path: pre-resolved JsVariable for let/const when no closures
                                // Only use after first iteration (which sets up the const binding properly)
                                cachedLetConstVariable.Write(nextJsValue);
                                break;
                            case true when !letConstFirstIterationDone:
                                plan.HandleFirstIterationAssignment(nextJsValue, reusableIterationEnvironment!,
                                    outerEnvironment, context, ref letConstFirstIterationDone);
                                break;
                            default:
                                {
                                    if (canUseSlotFastPath && iterationEnvironment.HasSlots)
                                    {
                                        // Fast path: direct slot write for let/const bindings
                                        iterationEnvironment.SetSlotDirect(fastPathSlotIndex, nextJsValue);
                                    }
                                    else
                                    {
                                        plan.Target.AssignLoopBinding(nextJsValue, iterationEnvironment,
                                            outerEnvironment, context,
                                            plan.DeclarationKind);
                                        if (context.IsThrow)
                                        {
                                            throw new ThrowSignal(context.FlowValue);
                                        }

                                        plan.SyncIterationSlots(iterationEnvironment, context);
                                    }

                                    break;
                                }
                        }
                    }

                    // Check if yield/await happened during binding (e.g., yield in destructuring default)
                    if (context.ShouldStopEvaluation)
                    {
                        break;
                    }

                    // Per ES spec 14.7.5.7 ForIn/OfBodyEvaluation step 5.k-l:
                    // Only update V (completion value) if result.[[Value]] is not empty
                    var bodyResult2 = plan.Body.EvaluateStatementJsValue(iterationEnvironment, context, loopLabel);
                    if (!bodyResult2.IsUnit)
                    {
                        lastValueJs = bodyResult2;
                    }

                    if (context.IsThrow)
                    {
                        throw new ThrowSignal(context.FlowValue);
                    }
                }

                if (context.IsReturn || context.IsThrow)
                {
                    break;
                }

                if (context.TryClearContinue(loopLabel))
                {
                    continue;
                }

                if (context.TryClearBreak(loopLabel))
                {
                    break;
                }
            }

            if (state.IteratorObject is not null && !iteratorDone)
            {
                state.IteratorObject.IteratorClose(context, context.IsThrow);
                if (context.IsThrow)
                {
                    return lastValueJs;
                }
            }

            return lastValueJs;
        }
        finally
        {
            // Return the reusable iteration environment to the pool if we created one
            if (reusableIterationEnvironment is not null)
            {
                JsEnvironmentPool.Return(reusableIterationEnvironment);
            }

            IteratorDriverStatePool.Return(state);
        }
    }

    /// <summary>
    /// Attempts to execute a fast for-of accumulator loop.
    /// Pattern: for (const n of arr) { sum += n; }
    /// This avoids calling EvaluateStatementJsValue per iteration.
    /// </summary>
    private static bool TryExecuteFastForOfAccumulator(this IteratorDriverPlan plan, IEnumerator<JsValue> enumerator,
        JsEnvironment loopEnvironment,
        JsEnvironment iterationEnvironment,
        int loopVarSlotIndex,
        out JsValue result)
    {
        result = JsValue.Undefined;

        // Body must be a block with exactly one statement
        if (plan.Body.Statements.Length != 1)
        {
            return false;
        }

        // That statement must be an expression statement with a compound assignment
        if (plan.Body.Statements[0] is not ExpressionStatement { Expression: AssignmentExpression assignExpr })
        {
            return false;
        }

        // Must be compound assignment (+=, -=, *=)
        if (!assignExpr.IsCompoundAssignment)
        {
            return false;
        }

        // For compound assignment sum += n, the Value is a BinaryExpression (sum + n)
        if (assignExpr.Value is not BinaryExpression binaryExpr)
        {
            return false;
        }

        // Determine operation type
        var operation = binaryExpr.Operator switch
        {
            BinaryOperator.Add => 0,
            BinaryOperator.Subtract => 1,
            BinaryOperator.Multiply => 2,
            _ => -1
        };

        if (operation < 0)
        {
            return false;
        }

        // Right of the operation should be the loop variable (the 'n' in 'sum += n')
        // Check if it's an identifier that references the loop variable slot
        if (binaryExpr.Right is not IdentifierExpression rhsId)
        {
            return false;
        }

        // The loop variable should have the same slot as our fast path slot
        if (plan.Target is not IdentifierBinding targetBinding ||
            !ReferenceEquals(rhsId.Name, targetBinding.Name))
        {
            return false;
        }

        var useSlotAccess = false;


        // Pre-resolve accumulator - try slot first, then dictionary fallback for top-level code
        JsEnvironment? accumEnv;
        if (loopEnvironment.TryResolveSlot(assignExpr.Target, assignExpr.ScopeId, assignExpr.SlotIndex,
                out accumEnv) &&
            accumEnv is not null)
        {
            useSlotAccess = true;
        }
        else
        {
            // Fallback: try to find accumulator via dictionary lookup (for top-level code)
            // The Target is already a Symbol (the accumulator variable name)
            // Find the environment containing the accumulator binding
            if (!loopEnvironment.TryLocateBindingInternal(assignExpr.Target, out accumEnv))
            {
                return false;
            }
        }

        if (accumEnv is null)
        {
            return false;
        }

        ref var loopVarRef = ref iterationEnvironment.GetSlotRef(loopVarSlotIndex);

        // The accumulator name is directly the Target (Symbol) of the assignment
        var accumName = assignExpr.Target;

        // Execute the tight loop - branch once on access method
        if (useSlotAccess)
        {
            // Slot-based fast path (function-scoped code)
            ref var accumRef = ref accumEnv.GetSlotRef(assignExpr.SlotIndex);
            if (!accumRef.IsNumber)
            {
                return false;
            }

            result = accumRef = RunAccumulatorLoop(enumerator, accumRef.NumberValue, operation, ref loopVarRef);
        }
        else
        {
            // Dictionary-based fallback (top-level code)
            var initialValue = accumEnv.GetBindingValueDirect(accumName);
            if (!initialValue.IsNumber)
            {
                return false;
            }

            result = RunAccumulatorLoop(enumerator, initialValue.NumberValue, operation, ref loopVarRef);
            accumEnv.SetBindingValueDirect(accumName, result);
        }

        return true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static JsValue RunAccumulatorLoop(
            IEnumerator<JsValue> enumerator,
            double initialValue,
            int operation,
            ref JsValue loopVarRef)
        {
            var accum = initialValue;

            while (enumerator.MoveNext())
            {
                var n = enumerator.Current;
                var nValue = n.IsNumber ? n.NumberValue : JsOps.ToNumber(n);
                loopVarRef = n;

                accum = operation switch
                {
                    0 => accum + nValue, // Add
                    1 => accum - nValue, // Subtract
                    2 => accum * nValue, // Multiply
                    _ => accum + nValue
                };
            }

            return new JsValue(accum);
        }
    }

    private static void SyncIterationSlots(this IteratorDriverPlan driverPlan, JsEnvironment iterationEnvironment,
        EvaluationContext context)
    {
        if (driverPlan.IterationSlotCount < 0 ||
            driverPlan.IterationScopeId < 0 ||
            driverPlan.PerIterationSlotIndices.IsDefaultOrEmpty ||
            driverPlan.PerIterationBindings.IsDefaultOrEmpty)
        {
            return;
        }

        if (iterationEnvironment.ScopeId != driverPlan.IterationScopeId)
        {
            return;
        }

        if (!iterationEnvironment.HasSlots)
        {
            return;
        }

        var slotIndices = driverPlan.PerIterationSlotIndices;
        var bindingNames = driverPlan.PerIterationBindings;
        var count = Math.Min(slotIndices.Length, bindingNames.Length);

        for (var i = 0; i < count; i++)
        {
            var slotIndex = slotIndices[i];
            if (slotIndex < 0)
            {
                continue;
            }

            var binding = bindingNames[i];
            if (!iterationEnvironment.TryGetIdentifierJsValue(binding, context, out var value))
            {
                continue;
            }

            iterationEnvironment.SetSlotDirect(slotIndex, value);
        }
    }

    /// <summary>
    /// Initializes an iteration environment so slot-based lookups hit the correct bindings.
    /// Uses cached slot map from the plan to avoid rebuilding it every iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InitializeIterationEnvironmentLayout(IteratorDriverPlan plan, JsEnvironment environment)
    {
        var requiredSlots = plan.GetRequiredSlots();
        if (requiredSlots < 0)
        {
            return;
        }

        environment.InitializeSlots(requiredSlots, plan.IterationScopeId);

        var slotMap = plan.GetOrCreateSlotMap();
        if (slotMap is not null && !slotMap.IsEmpty)
        {
            environment.SetSlotMap(slotMap);
        }
    }
}
