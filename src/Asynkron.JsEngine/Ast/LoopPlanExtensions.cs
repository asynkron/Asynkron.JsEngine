using System.Collections.Immutable;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(LoopPlan plan)
    {
        private object? EvaluateLoopPlan(JsEnvironment environment, EvaluationContext context,
            Symbol? loopLabel)
        {
            object? lastValue = Symbol.Undefined;

            if (context.AllowIdentifierCache && plan.LoopPlanHasDynamicScope())
            {
                context.AllowIdentifierCache = false;
            }

            if (!plan.LeadingStatements.IsDefaultOrEmpty)
            {
                foreach (var statement in plan.LeadingStatements)
                {
                    var leadingCompletion = EvaluateStatement(statement, environment, context, loopLabel);
                    if (context.ShouldStopEvaluation)
                    {
                        lastValue = leadingCompletion;
                        return NormalizeLoopCompletion(lastValue);
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

            while (true)
            {
                context.ThrowIfCancellationRequested();

            if (!plan.ConditionAfterBody)
            {
                if (!ExecuteCondition(plan, iterationEnvironment, context))
                {
                    break;
                }
            }

                lastValue = EvaluateStatement(plan.Body, iterationEnvironment, context, loopLabel);
                if (context.IsReturn || context.IsThrow)
                {
                    break;
                }

                if (context.TryClearContinue(loopLabel))
                {
                    // Create new per-iteration environment before increment
                    if (hasPerIterationBindings)
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

                // Create new per-iteration environment before increment
                if (hasPerIterationBindings)
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

            if (hasPerIterationBindings &&
                !ReferenceEquals(iterationEnvironment, environment))
            {
                if (allowIterationEnvPooling)
                {
                    JsEnvironmentPool.Return(iterationEnvironment);
                }
                // Otherwise keep the final iteration environment alive for any closures that captured it.
            }

            return NormalizeLoopCompletion(lastValue);
        }

        private JsEnvironment CreatePerIterationEnvironment(JsEnvironment currentIterationEnvironment,
            EvaluationContext context)
        {
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

            // Copy the per-iteration bindings from the CURRENT iteration environment to the new environment
            foreach (var bindingName in plan.PerIterationBindings)
            {
                // Get the current value from the current iteration environment.
                // Use direct identifier resolution with JsValue to avoid boxing primitives.
                JsValue currentValue;
                try
                {
                    currentValue = currentIterationEnvironment.GetIdentifierJsValue(bindingName, context);
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
                            errorObject = callable.Invoke([new JsValue(ex.Message)], JsValue.FromObject(Symbol.Undefined)).ToObject();
                        }
                        catch (ThrowSignal signal)
                        {
                            errorObject = signal.ThrownValue;
                        }
                    }

                    context.SetThrow(errorObject);
                    currentValue = JsValue.FromObject(errorObject);
                }

                var isConstBinding = currentIterationEnvironment.IsConstBinding(bindingName);

                // Define the binding in the new iteration environment using JsValue to avoid boxing
                // Use let semantics (isLexical=true, isConst=false by default, but the original
                // declaration kind doesn't matter for the copy)
                newIterationEnvironment.DefineJsValue(
                    bindingName,
                    currentValue,
                    isConst: isConstBinding,
                    isGlobalConstant: false,
                    isLexical: true,
                    blocksFunctionScopeOverride: false,
                    canDelete: false);
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

                var outerEnvironment = currentIterationEnvironment.Enclosing ?? currentIterationEnvironment;

                // Snapshot current values before we reset the environment instance.
                // Use JsValue[] to avoid boxing primitives.
                var count = bindings.Length;
                var values = new JsValue[count];
                var constFlags = new bool[count];

                for (var i = 0; i < count; i++)
                {
                    var bindingName = bindings[i];
                    JsValue currentValue;
                    try
                    {
                        currentValue = currentIterationEnvironment.GetIdentifierJsValue(bindingName, context);
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
                                errorObject = callable.Invoke([new JsValue(ex.Message)], JsValue.FromObject(Symbol.Undefined)).ToObject();
                            }
                            catch (ThrowSignal signal)
                            {
                                errorObject = signal.ThrownValue;
                            }
                        }

                        context.SetThrow(errorObject);
                        currentValue = JsValue.FromObject(errorObject);
                    }

                    values[i] = currentValue;
                    constFlags[i] = currentIterationEnvironment.IsConstBinding(bindingName);
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

                for (var i = 0; i < count; i++)
                {
                    var bindingName = bindings[i];
                    currentIterationEnvironment.DefineJsValue(
                        bindingName,
                        values[i],
                        isConst: constFlags[i],
                        isGlobalConstant: false,
                        isLexical: true,
                        blocksFunctionScopeOverride: false,
                        canDelete: false);
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
                    _ = EvaluateStatement(statement, environment, context);
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
                _ = EvaluateStatement(statement, environment, context);
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
    }
}
