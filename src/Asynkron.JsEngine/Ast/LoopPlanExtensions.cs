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
                        iterationEnvironment = plan.CreatePerIterationEnvironment(iterationEnvironment, context);
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
                    iterationEnvironment = plan.CreatePerIterationEnvironment(iterationEnvironment, context);
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

            return NormalizeLoopCompletion(lastValue);
        }

        private JsEnvironment CreatePerIterationEnvironment(JsEnvironment currentIterationEnvironment, EvaluationContext context)
        {
            // Per ECMAScript spec 13.7.4.9 CreatePerIterationEnvironment:
            // The new iteration environment's parent should be the OUTER environment (the loop environment),
            // not the current iteration environment
            var outerEnvironment = currentIterationEnvironment.Enclosing ?? currentIterationEnvironment;

            // Create a new environment for this iteration
            var newIterationEnvironment = new JsEnvironment(
                outerEnvironment,
                creatingSource: null,
                description: "for-iteration");

            // Copy the per-iteration bindings from the CURRENT iteration environment to the new environment
            foreach (var bindingName in plan.PerIterationBindings)
            {
                // Get the current value from the current iteration environment.
                // Use direct identifier resolution to avoid per-iteration IdentifierExpression allocations.
                object? currentValue;
                try
                {
                    currentValue = currentIterationEnvironment.GetIdentifierValue(bindingName, context);
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
                            errorObject = callable.Invoke([ex.Message], Symbol.Undefined);
                        }
                        catch (ThrowSignal signal)
                        {
                            errorObject = signal.ThrownValue;
                        }
                    }

                    context.SetThrow(errorObject);
                    currentValue = errorObject;
                }

                var isConstBinding = currentIterationEnvironment.IsConstBinding(bindingName);

                // Define the binding in the new iteration environment
                // Use let semantics (isLexical=true, isConst=false by default, but the original
                // declaration kind doesn't matter for the copy)
                newIterationEnvironment.Define(
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

            return IsTruthy(test);
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
    }
}
