using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(LoopPlan plan)
    {
        /// <summary>
        /// JsValue-returning version of EvaluateLoopPlan for use in hot paths.
        /// Avoids boxing on each iteration and at the final return.
        /// </summary>
        private JsValue EvaluateLoopPlanJsValue(JsEnvironment environment, EvaluationContext context,
            Symbol? loopLabel)
        {
            // Use JsValue to track the loop body result to avoid boxing on each iteration
            var lastValueJs = JsValue.Undefined;
            var logger = context.RealmState.Logger;
            var iterationIndex = 0;

            if (context.AllowIdentifierCache && plan.LoopPlanHasDynamicScope())
            {
                context.AllowIdentifierCache = false;
            }

            if (!plan.LeadingStatements.IsDefaultOrEmpty)
            {
                foreach (var statement in plan.LeadingStatements)
                {
                    // Leading statements (e.g., for loop initializer) are evaluated for side effects only.
                    // Per ES spec (14.7.4.7), the initializer does NOT contribute to the loop's completion value.
                    // The loop's completion value comes solely from the body; if the body never executes,
                    // the result is undefined.
                    _ = EvaluateStatementJsValue(statement, environment, context, loopLabel);
                    if (context.ShouldStopEvaluation)
                    {
                        return lastValueJs;
                    }
                }
            }

            // Check if we need per-iteration environments for lexical bindings
            var hasPerIterationBindings = !plan.PerIterationBindings.IsDefaultOrEmpty;
            var allowIterationEnvPooling = plan.AllowIterationEnvironmentPooling;

            // Per ECMAScript spec 13.7.4.8 ForBodyEvaluation step 2:
            // Create the first per-iteration environment BEFORE entering the loop
            var iterationEnvironment = hasPerIterationBindings
                ? plan.CreatePerIterationEnvironment(environment, context)
                : environment;

            // Fast path: if body is a single statement without block environment needs,
            // we can skip block dispatch overhead entirely (like Jint's ProbablyBlockStatement)
            var singleStatement = plan.SingleBodyStatement;

            while (true)
            {
                context.ThrowIfCancellationRequested();
                iterationIndex++;

                if (logger is not null && ShouldLogIteration(iterationIndex))
                {
                    if (plan.PerIterationBindings.IsDefaultOrEmpty)
                    {
                        logger.LogInformation("Loop iteration {Iteration}: (no per-iteration bindings)", iterationIndex);
                    }
                    else
                    {
                        var parts = ArrayPool<string>.Shared.Rent(plan.PerIterationBindings.Length);
                        var partCount = 0;
                        foreach (var binding in plan.PerIterationBindings)
                        {
                            if (iterationEnvironment.TryGetIdentifierJsValue(binding, context, out var value))
                            {
                                parts[partCount++] = $"{binding.Name}={value}";
                            }
                        }

                        logger.LogInformation(
                            "Loop iteration {Iteration}: {Bindings}",
                            iterationIndex,
                            string.Join(", ", parts.AsSpan(0, partCount).ToArray()));

                        ArrayPool<string>.Shared.Return(parts, clearArray: true);
                    }
                }

                if (!plan.ConditionAfterBody)
                {
                    if (!ExecuteCondition(plan, iterationEnvironment, context))
                    {
                        break;
                    }
                }

                // Fast path: execute single statement directly without block overhead
                // Note: We do NOT pass loopLabel to inner statements - the block evaluation doesn't either.
                // Inner loops get their own labels via LabeledStatement. Passing our loopLabel would cause
                // inner loops to incorrectly handle labeled breaks/continues meant for the outer loop.
                JsValue bodyResult;
                if (singleStatement is not null)
                {
                    bodyResult = EvaluateStatementJsValue(singleStatement, iterationEnvironment, context);
                }
                else
                {
                    bodyResult = EvaluateStatementJsValue(plan.Body, iterationEnvironment, context, loopLabel);
                }

                // Apply UpdateEmpty semantics (ES spec 13.7.3.6 step 2.f):
                // Only update the completion value if body returned a non-empty value.
                // This preserves the previous completion value when break/continue has empty completion.
                if (!bodyResult.IsUnit)
                {
                    lastValueJs = bodyResult;
                }

                if (context.IsReturn || context.IsThrow)
                {
                    break;
                }

                if (context.TryClearContinue(loopLabel))
                {
                    // Create new per-iteration environment before increment, but only if there are closures
                    // that might capture loop variable values. When allowIterationEnvPooling is true, no closures
                    // exist, so we can skip the expensive environment refresh and just mutate bindings in place.
                    if (hasPerIterationBindings && !allowIterationEnvPooling)
                    {
                        iterationEnvironment = plan.CreateNextIterationEnvironment(iterationEnvironment, context);
                    }

                    if (!ExecutePostIteration(plan, iterationEnvironment, context))
                    {
                        break;
                    }

                    if (plan.ConditionAfterBody && !ExecuteCondition(plan, iterationEnvironment, context))
                    {
                        break;
                    }

                    continue;
                }

                if (context.TryClearBreak(loopLabel))
                {
                    break;
                }

                if (context.ShouldStopEvaluation)
                {
                    break;
                }

                // Create new per-iteration environment before increment, but only if there are closures
                // that might capture loop variable values. When allowIterationEnvPooling is true, no closures
                // exist, so we can skip the expensive environment refresh and just mutate bindings in place.
                if (hasPerIterationBindings && !allowIterationEnvPooling)
                {
                    iterationEnvironment = plan.CreateNextIterationEnvironment(iterationEnvironment, context);
                }

                if (!ExecutePostIteration(plan, iterationEnvironment, context))
                {
                    break;
                }

                if (!plan.ConditionAfterBody)
                {
                    continue;
                }

                if (!ExecuteCondition(plan, iterationEnvironment, context))
                {
                    break;
                }
            }

            static bool ShouldLogIteration(int iterationIndex)
            {
                return iterationIndex <= 10 || (iterationIndex & (iterationIndex - 1)) == 0;
            }

            if (hasPerIterationBindings &&
                !ReferenceEquals(iterationEnvironment, environment))
            {
                if (allowIterationEnvPooling)
                {
                    JsEnvironmentPool.Return(iterationEnvironment);
                }
                // Otherwise keep the final iteration environment alive for any closures that captured it.
            }

            return lastValueJs;
        }

        private JsEnvironment CreatePerIterationEnvironment(JsEnvironment currentIterationEnvironment,
            EvaluationContext context)
        {
            var iterationScopeId = plan.IterationScopeId;
            var iterationSlotCount = plan.IterationSlotCount;
            var iterationSlotIndices = plan.PerIterationSlotIndices;

            // Per ECMAScript spec 13.7.4.9 CreatePerIterationEnvironment:
            // The new iteration environment's parent should be the OUTER environment (the loop environment),
            // not the current iteration environment
            var outerEnvironment = currentIterationEnvironment.Enclosing ?? currentIterationEnvironment;

            // Create a fresh environment for this iteration
            var newIterationEnvironment = plan.AllowIterationEnvironmentPooling
                ? JsEnvironmentPool.Rent(
                    outerEnvironment,
                    isFunctionScope: false,
                    isStrict: false,
                    creatingSource: null,
                    description: "for-iteration")
                : new JsEnvironment(
                    outerEnvironment,
                    isFunctionScope: false,
                    isStrict: false,
                    creatingSource: null,
                    description: "for-iteration");

            if (iterationSlotCount >= 0)
            {
                newIterationEnvironment.InitializeSlots(iterationSlotCount, iterationScopeId);
            }

            // Copy the per-iteration bindings from the CURRENT iteration environment to the new environment
            for (var i = 0; i < plan.PerIterationBindings.Length; i++)
            {
                var bindingName = plan.PerIterationBindings[i];
                // Get the current value from the current iteration environment.
                // Use direct identifier resolution with JsValue to avoid boxing primitives.
                JsValue currentValue;
                try
                {
                    currentValue = context.GetIdentifier(currentIterationEnvironment, bindingName);
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                           StringComparison.Ordinal))
                {
                    object? errorObject = ex.Message;

                    if (currentIterationEnvironment.TryGet(Symbol.ReferenceErrorIdentifier, out var ctor) &&
                        ctor is IJsCallable callable)
                    {
                        try
                        {
                            errorObject = callable.Invoke([new JsValue(ex.Message)], JsValue.Undefined).ToObject();
                        }
                        catch (ThrowSignal signal)
                        {
                            errorObject = signal.ThrownValue;
                        }
                    }

                    context.SetThrow(JsValue.FromObjectUnsafe(errorObject));
                    currentValue = JsValue.FromObjectUnsafe(errorObject);
                }

                var isConstBinding = currentIterationEnvironment.IsConstBinding(bindingName);

                newIterationEnvironment.DefineJsValue(
                    bindingName,
                    currentValue,
                    isConst: isConstBinding,
                    isGlobalConstant: false,
                    isLexical: true,
                    blocksFunctionScopeOverride: false,
                    canDelete: false);

                if (iterationSlotCount >= 0 && newIterationEnvironment.ScopeId == iterationScopeId && !iterationSlotIndices.IsDefaultOrEmpty)
                {
                    var targetSlot = iterationSlotIndices.Length > i ? iterationSlotIndices[i] : -1;
                    if (targetSlot >= 0 && newIterationEnvironment.HasSlots)
                    {
                        newIterationEnvironment.SetSlot(0, targetSlot, currentValue);
                    }
                }
            }

            return newIterationEnvironment;
        }

        private JsEnvironment CreateNextIterationEnvironment(
            JsEnvironment currentIterationEnvironment,
            EvaluationContext context)
        {
            if (plan.AllowIterationEnvironmentPooling)
            {
                var bindings = plan.PerIterationBindings;
                if (bindings.IsDefaultOrEmpty)
                {
                    return currentIterationEnvironment;
                }

                const int StackAllocLimit = 8;

                var outerEnvironment = currentIterationEnvironment.Enclosing ?? currentIterationEnvironment;

                // Snapshot current values before we reset the environment instance.
                // Use pooled buffers to avoid per-iteration heap allocations.
                var count = bindings.Length;

                // JsValue is a managed type (contains object reference) and cannot be stack-allocated
                var rentedValues = ArrayPool<JsValue>.Shared.Rent(count);
                var valueSpan = rentedValues.AsSpan(0, count);

                bool[]? rentedConstFlags = null;
                var constFlagSpan = count <= StackAllocLimit
                    ? stackalloc bool[StackAllocLimit]
                    : (rentedConstFlags = ArrayPool<bool>.Shared.Rent(count)).AsSpan(0, count);

                for (var i = 0; i < count; i++)
                {
                    var bindingName = bindings[i];
                    JsValue currentValue;
                    try
                    {
                        currentValue = context.GetIdentifier(currentIterationEnvironment, bindingName);
                    }
                    catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                               StringComparison.Ordinal))
                    {
                        object? errorObject = ex.Message;

                        if (currentIterationEnvironment.TryGet(Symbol.ReferenceErrorIdentifier, out var ctor) &&
                            ctor is IJsCallable callable)
                        {
                            try
                            {
                                errorObject = callable.Invoke([new JsValue(ex.Message)], JsValue.Undefined).ToObject();
                            }
                            catch (ThrowSignal signal)
                            {
                                errorObject = signal.ThrownValue;
                            }
                        }

                        context.SetThrow(JsValue.FromObjectUnsafe(errorObject));
                        currentValue = JsValue.FromObjectUnsafe(errorObject);
                    }

                    valueSpan[i] = currentValue;
                    constFlagSpan[i] = currentIterationEnvironment.IsConstBinding(bindingName);
                }

                // Reset the environment in place to mimic a fresh per-iteration lexical environment,
                // but keep the enclosing/scope metadata intact.
                currentIterationEnvironment.Reset(
                    outerEnvironment,
                    isFunctionScope: false,
                    isStrict: false,
                    creatingSource: null,
                    description: "for-iteration",
                    isParameterEnvironment: false,
                    isBodyEnvironment: false);

                if (plan.IterationSlotCount >= 0)
                {
                    currentIterationEnvironment.InitializeSlots(plan.IterationSlotCount, plan.IterationScopeId);
                }

                for (var i = 0; i < count; i++)
                {
                    var bindingName = bindings[i];
                    var slotIndex = -1;
                    if (plan.IterationSlotCount >= 0 && currentIterationEnvironment.ScopeId == plan.IterationScopeId)
                    {
                        slotIndex = plan.PerIterationSlotIndices.Length > i ? plan.PerIterationSlotIndices[i] : -1;
                        if (slotIndex >= 0 && currentIterationEnvironment.HasSlots)
                        {
                            currentIterationEnvironment.SetSlot(0, slotIndex, valueSpan[i]);
                        }
                    }

                    currentIterationEnvironment.DefineJsValue(
                        bindingName,
                        valueSpan[i],
                        isConst: constFlagSpan[i],
                        isGlobalConstant: false,
                        isLexical: true,
                        blocksFunctionScopeOverride: false,
                        canDelete: false);

                    if (plan.IterationSlotCount >= 0 && currentIterationEnvironment.ScopeId == plan.IterationScopeId &&
                        !plan.PerIterationSlotIndices.IsDefaultOrEmpty)
                    {
                        if (slotIndex >= 0 && currentIterationEnvironment.HasSlots)
                        {
                            currentIterationEnvironment.SetSlot(0, slotIndex, valueSpan[i]);
                        }
                    }
                }

                ArrayPool<JsValue>.Shared.Return(rentedValues, clearArray: true);

                if (rentedConstFlags is not null)
                {
                    ArrayPool<bool>.Shared.Return(rentedConstFlags, clearArray: true);
                }

                return currentIterationEnvironment;
            }

            // Create a new env using the outer of the current iteration env
            var next = plan.CreatePerIterationEnvironment(currentIterationEnvironment, context);

            if (plan.AllowIterationEnvironmentPooling &&
                !ReferenceEquals(currentIterationEnvironment, currentIterationEnvironment.Enclosing))
            {
                JsEnvironmentPool.Return(currentIterationEnvironment);
            }

            return next;
        }

        private bool ExecuteCondition(JsEnvironment environment, EvaluationContext context)
        {
            if (!plan.ConditionPrologue.IsDefaultOrEmpty)
            {
                foreach (var statement in plan.ConditionPrologue)
                {
                    _ = EvaluateStatementJsValue(statement, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return false;
                    }
                }
            }

            var test = EvaluateExpression(plan.Condition, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            return test.IsTruthy;
        }

        private bool ExecutePostIteration(JsEnvironment environment, EvaluationContext context)
        {
            if (plan.PostIteration.IsDefaultOrEmpty)
            {
                return true;
            }

            foreach (var statement in plan.PostIteration)
            {
                // Post-iteration steps usually contain a simple expression (e.g., i++).
                // We don't need its completion value, so evaluate expression statements
                // directly to avoid ToObject/GetNumber boxing on every iteration.
                if (statement is ExpressionStatement expr)
                {
                    _ = EvaluateExpression(expr.Expression, environment, context);
                }
                else
                {
                    _ = EvaluateStatementJsValue(statement, environment, context);
                }
                if (context.ShouldStopEvaluation)
                {
                    return false;
                }
            }

            return true;
        }

        private bool LoopPlanHasDynamicScope()
        {
            if (!AllowsIdentifierCaching(plan.Body))
            {
                return true;
            }

            if (StatementsContainDynamicScope(plan.LeadingStatements) ||
                StatementsContainDynamicScope(plan.ConditionPrologue) ||
                StatementsContainDynamicScope(plan.PostIteration))
            {
                return true;
            }

            if (plan.Condition is not null && ContainsDirectEval(plan.Condition))
            {
                return true;
            }

            return false;
        }

        private static bool StatementsContainDynamicScope(ImmutableArray<StatementNode> statements)
        {
            if (statements.IsDefaultOrEmpty)
            {
                return false;
            }

            var synthetic = new BlockStatement(null, statements, false);
            return ContainsWithOrDirectEval(synthetic);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetSlotIndex(ImmutableArray<int> slotIndices, Symbol binding, ImmutableArray<Symbol> bindingNames)
        {
            if (slotIndices.IsDefaultOrEmpty || bindingNames.IsDefaultOrEmpty || slotIndices.Length != bindingNames.Length)
            {
                return -1;
            }

            for (var i = 0; i < bindingNames.Length; i++)
            {
                if (ReferenceEquals(bindingNames[i], binding))
                {
                    return slotIndices[i];
                }
            }

            return -1;
        }
    }
}
