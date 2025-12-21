using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Represents a resolved reference to a JavaScript variable within its lexical scope.
/// Provides fast read/write access by holding the environment and slot index.
/// </summary>
internal readonly struct JsVariable(JsEnvironment environment, int slotIndex)
{
    public readonly JsEnvironment Environment = environment;
    public readonly int SlotIndex = slotIndex;

    public bool IsValid => Environment is not null && SlotIndex >= 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsValue Read() => Environment.GetSlotRef(SlotIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(JsValue value) => Environment.GetSlotRef(SlotIndex) = value;
}

/// <summary>
/// Comparison type for fast loop conditions.
/// </summary>
internal enum FastLoopComparison
{
    LessThan,           // i < limit (ascending)
    LessThanOrEqual,    // i <= limit (ascending)
    GreaterThan,        // i > limit (descending)
    GreaterThanOrEqual  // i >= limit (descending)
}

/// <summary>
/// Arithmetic operation for fast loop accumulators.
/// </summary>
internal enum FastLoopOperation
{
    Add,      // s += i
    Subtract, // s -= i
    Multiply  // s *= i
}

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
            var hasPerIterationBindings = !plan.PerIterationBindings.IsDefaultOrEmpty;

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
                    _ = statement.EvaluateStatementJsValue(environment, context, loopLabel);
                    if (context.ShouldStopEvaluation)
                    {
                        return lastValueJs;
                    }
                }
            }

            // Check if we need per-iteration environments for lexical bindings
            var allowIterationEnvPooling = plan.AllowIterationEnvironmentPooling;

            // Per ECMAScript spec 13.7.4.8 ForBodyEvaluation step 2:
            // Create the first per-iteration environment BEFORE entering the loop
            var iterationEnvironment = hasPerIterationBindings
                ? plan.CreatePerIterationEnvironment(environment, context)
                : environment;

            // Fast path: if body is a single statement without block environment needs,
            // we can skip block dispatch overhead entirely (like Jint's ProbablyBlockStatement)
            var singleStatement = plan.SingleBodyStatement;

            // Try ultra-fast path for simple numeric for loops
            // Pattern: for (let i = 0; i < limit; i++) { s += i; }
            if (plan.TryExecuteFastNumericLoop(iterationEnvironment, context, loopLabel, out var fastResult))
            {
                return fastResult;
            }

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
                    if (!plan.ExecuteCondition(iterationEnvironment, context))
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
                    bodyResult = singleStatement.EvaluateStatementJsValue(iterationEnvironment, context);
                }
                else
                {
                    bodyResult = plan.Body.EvaluateStatementJsValue(iterationEnvironment, context, loopLabel);
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

                    if (!plan.ExecutePostIteration(iterationEnvironment, context))
                    {
                        break;
                    }

                    if (plan.ConditionAfterBody && !plan.ExecuteCondition(iterationEnvironment, context))
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

                if (!plan.ExecutePostIteration(iterationEnvironment, context))
                {
                    break;
                }

                if (!plan.ConditionAfterBody)
                {
                    continue;
                }

                if (!plan.ExecuteCondition(iterationEnvironment, context))
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
            // Fast path: use direct slot access when slot indices are available
            var canUseSlotFastPath = iterationSlotCount >= 0 &&
                                     !iterationSlotIndices.IsDefaultOrEmpty &&
                                     currentIterationEnvironment.HasSlots &&
                                     currentIterationEnvironment.ScopeId == iterationScopeId;

            for (var i = 0; i < plan.PerIterationBindings.Length; i++)
            {
                var bindingName = plan.PerIterationBindings[i];
                var sourceSlotIndex = iterationSlotIndices.Length > i ? iterationSlotIndices[i] : -1;

                // Fast path: direct slot read avoids scope chain traversal
                JsValue currentValue;
                if (canUseSlotFastPath && sourceSlotIndex >= 0)
                {
                    currentValue = currentIterationEnvironment.GetSlotRef(sourceSlotIndex);
                }
                else
                {
                    // Fallback: full identifier resolution
                    try
                    {
                        currentValue = context.GetIdentifier(currentIterationEnvironment, bindingName);
                    }
                    catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                               StringComparison.Ordinal))
                    {
                        JsValue errorValue = new JsValue(ex.Message);

                        if (currentIterationEnvironment.TryGet(Symbol.ReferenceErrorIdentifier, out var ctor) &&
                            ctor is IJsCallable callable)
                        {
                            try
                            {
                                errorValue = callable.Invoke([new JsValue(ex.Message)], JsValue.Undefined);
                            }
                            catch (ThrowSignal signal)
                            {
                                errorValue = signal.ThrownValue;
                            }
                        }

                        context.SetThrow(errorValue);
                        currentValue = errorValue;
                    }
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
                    var targetSlot = sourceSlotIndex;
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
                var slotIndices = plan.PerIterationSlotIndices;

                // Fast path: use direct slot access when slot indices are available
                var canUseSlotFastPath = plan.IterationSlotCount >= 0 &&
                                         !slotIndices.IsDefaultOrEmpty &&
                                         currentIterationEnvironment.HasSlots &&
                                         currentIterationEnvironment.ScopeId == plan.IterationScopeId;

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
                    var sourceSlotIndex = slotIndices.Length > i ? slotIndices[i] : -1;

                    // Fast path: direct slot read avoids scope chain traversal
                    JsValue currentValue;
                    if (canUseSlotFastPath && sourceSlotIndex >= 0)
                    {
                        currentValue = currentIterationEnvironment.GetSlotRef(sourceSlotIndex);
                    }
                    else
                    {
                        // Fallback: full identifier resolution
                        try
                        {
                            currentValue = context.GetIdentifier(currentIterationEnvironment, bindingName);
                        }
                        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                                   StringComparison.Ordinal))
                        {
                            JsValue errorValue = new JsValue(ex.Message);

                            if (currentIterationEnvironment.TryGet(Symbol.ReferenceErrorIdentifier, out var ctor) &&
                                ctor is IJsCallable callable)
                            {
                                try
                                {
                                    errorValue = callable.Invoke([new JsValue(ex.Message)], JsValue.Undefined);
                                }
                                catch (ThrowSignal signal)
                                {
                                    errorValue = signal.ThrownValue;
                                }
                            }

                            context.SetThrow(errorValue);
                            currentValue = errorValue;
                        }
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
                    _ = statement.EvaluateStatementJsValue(environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return false;
                    }
                }
            }

            var test = plan.Condition.EvaluateExpression(environment, context);
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
                    _ = expr.Expression.EvaluateExpression(environment, context);
                }
                else
                {
                    _ = statement.EvaluateStatementJsValue(environment, context);
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

            if (LoopPlan.StatementsContainDynamicScope(plan.LeadingStatements) || LoopPlan.StatementsContainDynamicScope(plan.ConditionPrologue) || LoopPlan.StatementsContainDynamicScope(plan.PostIteration))
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

        /// <summary>
        /// Tries to execute a simple numeric loop using a fast path that bypasses AST evaluation.
        /// Supports: for, while, do-while with various comparison and arithmetic operators.
        /// </summary>
        private bool TryExecuteFastNumericLoop(
            JsEnvironment environment,
            EvaluationContext context,
            Symbol? loopLabel,
            out JsValue result)
        {
            result = JsValue.Undefined;

            // Only attempt fast path for simple loops without dynamic scope or per-iteration environments
            if (!plan.AllowIterationEnvironmentPooling ||
                !plan.ConditionPrologue.IsDefaultOrEmpty ||
                loopLabel is not null)
            {
                return false;
            }

            // Extract condition pattern: identifier <op> constant or constant <op> identifier
            if (!plan.TryExtractConditionPattern(out var loopVarId, out var limit, out var comparison))
            {
                return false;
            }

            // Determine expected increment/decrement direction
            var expectIncrement = comparison is FastLoopComparison.LessThan or FastLoopComparison.LessThanOrEqual;

            // Try for-loop pattern: post-iteration has i++ or i--
            if (plan.TryMatchForLoopPattern(loopVarId, limit, comparison, expectIncrement, environment, out result))
            {
                return true;
            }

            // Try while/do-while pattern: body has { s <op>= i; i++/i--; }
            if (plan.TryMatchWhileLoopPattern(loopVarId, limit, comparison, expectIncrement, environment, out result))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Extracts the condition pattern from the loop.
        /// Supports: i &lt; n, i &lt;= n, i &gt; n, i &gt;= n (and reversed: n &gt; i, etc.)
        /// </summary>
        private bool TryExtractConditionPattern(
            out IdentifierExpression loopVarId,
            out double limit,
            out FastLoopComparison comparison)
        {
            loopVarId = null!;
            limit = 0;
            comparison = default;

            if (plan.Condition is not BinaryExpression condBinary)
            {
                return false;
            }

            // Pattern: identifier <op> literal
            if (condBinary.Left is IdentifierExpression leftId &&
                leftId.ScopeId >= 0 && leftId.SlotIndex >= 0 &&
                condBinary.Right is LiteralExpression { Value.IsNumber: true } rightLit)
            {
                loopVarId = leftId;
                limit = rightLit.Value.NumberValue;
                comparison = condBinary.Operator switch
                {
                    BinaryOperator.LessThan => FastLoopComparison.LessThan,
                    BinaryOperator.LessThanOrEqual => FastLoopComparison.LessThanOrEqual,
                    BinaryOperator.GreaterThan => FastLoopComparison.GreaterThan,
                    BinaryOperator.GreaterThanOrEqual => FastLoopComparison.GreaterThanOrEqual,
                    _ => (FastLoopComparison)(-1)
                };
                return (int)comparison >= 0;
            }

            // Pattern: literal <op> identifier (reversed)
            if (condBinary.Right is IdentifierExpression rightId &&
                rightId.ScopeId >= 0 && rightId.SlotIndex >= 0 &&
                condBinary.Left is LiteralExpression { Value.IsNumber: true } leftLit)
            {
                loopVarId = rightId;
                limit = leftLit.Value.NumberValue;
                // Reverse the comparison: (5 > i) means (i < 5)
                comparison = condBinary.Operator switch
                {
                    BinaryOperator.LessThan => FastLoopComparison.GreaterThan,
                    BinaryOperator.LessThanOrEqual => FastLoopComparison.GreaterThanOrEqual,
                    BinaryOperator.GreaterThan => FastLoopComparison.LessThan,
                    BinaryOperator.GreaterThanOrEqual => FastLoopComparison.LessThanOrEqual,
                    _ => (FastLoopComparison)(-1)
                };
                return (int)comparison >= 0;
            }

            return false;
        }

        /// <summary>
        /// Matches for-loop pattern: for (let i = 0; i &lt; limit; i++) { s += i; }
        /// PostIteration contains i++ or i--, body is single s &lt;op&gt;= i statement.
        /// </summary>
        private bool TryMatchForLoopPattern(
            IdentifierExpression loopVarId,
            double limit,
            FastLoopComparison comparison,
            bool expectIncrement,
            JsEnvironment environment,
            out JsValue result)
        {
            result = JsValue.Undefined;

            // Check post-iteration pattern: i++ or i--
            if (plan.PostIteration.Length != 1 ||
                plan.PostIteration[0] is not ExpressionStatement { Expression: UnaryExpression unaryExpr } ||
                unaryExpr.Operand is not IdentifierExpression postId ||
                !ReferenceEquals(postId.Name, loopVarId.Name))
            {
                return false;
            }

            var isIncrement = unaryExpr.Operator == UnaryOperator.Increment;
            var isDecrement = unaryExpr.Operator == UnaryOperator.Decrement;
            if (!isIncrement && !isDecrement)
            {
                return false;
            }

            // Verify direction matches expectation
            if (expectIncrement != isIncrement)
            {
                return false;
            }

            // Check body pattern: single expression statement with compound assignment
            var bodyStatement = plan.SingleBodyStatement;
            if (bodyStatement is not ExpressionStatement { Expression: AssignmentExpression assignExpr })
            {
                return false;
            }

            if (!LoopPlan.TryExtractAccumulatorPattern(assignExpr, loopVarId, out var accumScopeId, out var accumSlotIndex, out var operation))
            {
                return false;
            }

            return LoopPlan.ExecuteFastNumericLoop(loopVarId, limit, comparison, isIncrement, accumScopeId, accumSlotIndex, operation, plan.ConditionAfterBody, environment, out result);
        }

        /// <summary>
        /// Matches while/do-while pattern: while (i &lt; limit) { s += i; i++; }
        /// PostIteration is empty, body contains both s &lt;op&gt;= i and i++/i--.
        /// </summary>
        private bool TryMatchWhileLoopPattern(
            IdentifierExpression loopVarId,
            double limit,
            FastLoopComparison comparison,
            bool expectIncrement,
            JsEnvironment environment,
            out JsValue result)
        {
            result = JsValue.Undefined;

            // While/do-while loops have empty PostIteration
            if (!plan.PostIteration.IsDefaultOrEmpty)
            {
                return false;
            }

            // Body must have exactly 2 statements
            if (plan.Body.Statements.Length != 2)
            {
                return false;
            }

            // First statement: s <op>= i (compound assignment)
            if (plan.Body.Statements[0] is not ExpressionStatement { Expression: AssignmentExpression assignExpr })
            {
                return false;
            }

            if (!LoopPlan.TryExtractAccumulatorPattern(assignExpr, loopVarId, out var accumScopeId, out var accumSlotIndex, out var operation))
            {
                return false;
            }

            // Second statement: i++ or i--
            if (plan.Body.Statements[1] is not ExpressionStatement { Expression: UnaryExpression unaryExpr } ||
                unaryExpr.Operand is not IdentifierExpression incrId ||
                !ReferenceEquals(incrId.Name, loopVarId.Name))
            {
                return false;
            }

            var isIncrement = unaryExpr.Operator == UnaryOperator.Increment;
            var isDecrement = unaryExpr.Operator == UnaryOperator.Decrement;
            if (!isIncrement && !isDecrement)
            {
                return false;
            }

            // Verify direction matches expectation
            if (expectIncrement != isIncrement)
            {
                return false;
            }

            return LoopPlan.ExecuteFastNumericLoop(loopVarId, limit, comparison, isIncrement, accumScopeId, accumSlotIndex, operation, plan.ConditionAfterBody, environment, out result);
        }

        /// <summary>
        /// Extracts accumulator slot info from a compound assignment expression (s += i, s -= i, s *= i).
        /// </summary>
        private static bool TryExtractAccumulatorPattern(
            AssignmentExpression assignExpr,
            IdentifierExpression loopVarId,
            out int accumScopeId,
            out int accumSlotIndex,
            out FastLoopOperation operation)
        {
            accumScopeId = -1;
            accumSlotIndex = -1;
            operation = default;

            // Must be compound assignment
            if (!assignExpr.IsCompoundAssignment)
            {
                return false;
            }

            // Get the accumulator slot info from the assignment target
            if (assignExpr.ScopeId < 0 || assignExpr.SlotIndex < 0)
            {
                return false;
            }

            // For compound assignment s <op>= i, the Value is a BinaryExpression (s <op> i)
            if (assignExpr.Value is not BinaryExpression binaryExpr)
            {
                return false;
            }

            // Determine operation type
            operation = binaryExpr.Operator switch
            {
                BinaryOperator.Add => FastLoopOperation.Add,
                BinaryOperator.Subtract => FastLoopOperation.Subtract,
                BinaryOperator.Multiply => FastLoopOperation.Multiply,
                _ => (FastLoopOperation)(-1)
            };

            if ((int)operation < 0)
            {
                return false;
            }

            // Left of the operation should be the accumulator (same as target)
            if (binaryExpr.Left is not IdentifierExpression accumIdExpr ||
                !ReferenceEquals(accumIdExpr.Name, assignExpr.Target))
            {
                return false;
            }

            // Right of the operation should be the loop variable
            if (binaryExpr.Right is not IdentifierExpression rhsId ||
                !ReferenceEquals(rhsId.Name, loopVarId.Name))
            {
                return false;
            }

            accumScopeId = assignExpr.ScopeId;
            accumSlotIndex = assignExpr.SlotIndex;
            return true;
        }

        /// <summary>
        /// Executes the tight numeric loop with pre-resolved slot references.
        /// Supports all comparison operators, increment/decrement, and arithmetic operations.
        /// </summary>
        private static bool ExecuteFastNumericLoop(
            IdentifierExpression loopVarId,
            double limit,
            FastLoopComparison comparison,
            bool isIncrement,
            int accumScopeId,
            int accumSlotIndex,
            FastLoopOperation operation,
            bool conditionAfterBody,
            JsEnvironment environment,
            out JsValue result)
        {
            result = JsValue.Undefined;

            // Pre-resolve slots
            if (!environment.TryResolveSlot(loopVarId.ScopeId, loopVarId.SlotIndex, out var loopVarEnv) ||
                loopVarEnv is null)
            {
                return false;
            }

            if (!environment.TryResolveSlot(accumScopeId, accumSlotIndex, out var accumEnv) ||
                accumEnv is null)
            {
                return false;
            }

            ref var loopVarRef = ref loopVarEnv.GetSlotRef(loopVarId.SlotIndex);
            ref var accumRef = ref accumEnv.GetSlotRef(accumSlotIndex);

            var lastValue = JsValue.Undefined;

            // Select the appropriate tight loop based on comparison and operation
            if (conditionAfterBody)
            {
                // do-while: execute body first, then check condition
                do
                {
                    var i = loopVarRef.NumberValue;
                    var s = accumRef.NumberValue;

                    var newAccum = operation switch
                    {
                        FastLoopOperation.Add => s + i,
                        FastLoopOperation.Subtract => s - i,
                        FastLoopOperation.Multiply => s * i,
                        _ => s + i
                    };

                    accumRef = new JsValue(newAccum);
                    lastValue = accumRef;
                    loopVarRef = new JsValue(isIncrement ? i + 1 : i - 1);

                } while (LoopPlan.CheckCondition(loopVarRef.NumberValue, limit, comparison));
            }
            else
            {
                // while/for: check condition first
                while (LoopPlan.CheckCondition(loopVarRef.NumberValue, limit, comparison))
                {
                    var i = loopVarRef.NumberValue;
                    var s = accumRef.NumberValue;

                    var newAccum = operation switch
                    {
                        FastLoopOperation.Add => s + i,
                        FastLoopOperation.Subtract => s - i,
                        FastLoopOperation.Multiply => s * i,
                        _ => s + i
                    };

                    accumRef = new JsValue(newAccum);
                    lastValue = accumRef;
                    loopVarRef = new JsValue(isIncrement ? i + 1 : i - 1);
                }
            }

            result = lastValue;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CheckCondition(double loopVar, double limit, FastLoopComparison comparison)
        {
            return comparison switch
            {
                FastLoopComparison.LessThan => loopVar < limit,
                FastLoopComparison.LessThanOrEqual => loopVar <= limit,
                FastLoopComparison.GreaterThan => loopVar > limit,
                FastLoopComparison.GreaterThanOrEqual => loopVar >= limit,
                _ => false
            };
        }
    }
}
