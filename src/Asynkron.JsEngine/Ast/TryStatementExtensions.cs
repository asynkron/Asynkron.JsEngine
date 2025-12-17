using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(TryStatement statement)
    {
        /// <summary>
        /// JsValue-returning version for use in hot paths.
        /// </summary>
        private JsValue EvaluateTryJsValue(JsEnvironment environment, EvaluationContext context)
        {
            context.RealmState.Logger?.LogInformation(
                "EvaluateTry enter (catch={HasCatch}, finally={HasFinally}) throwFlag={ThrowFlag}",
                statement.Catch is not null,
                statement.Finally is not null,
                context.IsThrow);

            JsValue result;
            try
            {
                result = EvaluateBlockJsValue(statement.TryBlock, environment, context);
            }
            catch (ThrowSignal signal)
            {
                // Normalize abrupt throws into the current context so catch/finally
                // handling runs in the standard completion-flow path.
                context.SetThrow(signal.ThrownValue);
                result = signal.ThrownValue;
            }
            if (context.IsThrow && statement.Catch is not null)
            {
                context.RealmState.Logger?.LogInformation(
                    "EvaluateTry handling catch; throw type={ThrowType}",
                    !context.FlowValue.IsUndefined ? context.FlowValue.GetType().Name : "undefined");
                var thrownValue = context.FlowValue;
                context.Clear();
                var catchEnv = new JsEnvironment(environment, creatingSource: statement.Catch.Body.Source,
                    description: "catch");

                // ES2019: Optional catch binding - only bind if a parameter was provided
                if (statement.Catch.Binding is not null)
                {
                    if (statement.Catch.Binding is IdentifierBinding identifierBinding)
                    {
                        catchEnv.SetSimpleCatchParameters(
                            new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance) { identifierBinding.Name });
                    }

                    DefineBindingTarget(statement.Catch.Binding, thrownValue, catchEnv, context, false);
                }

                result = EvaluateBlockJsValue(statement.Catch.Body, catchEnv, context);
            }

            if (statement.Finally is null)
            {
                context.RealmState.Logger?.LogInformation(
                    "EvaluateTry exit (no finally) throwFlag={ThrowFlag}",
                    context.IsThrow);
                // Per ES spec 14.15.2 TryStatement Evaluation:
                // If C.[[value]] is not empty, return Completion(C).
                // Return Completion{[[type]]: C.[[type]], [[value]]: undefined, [[target]]: C.[[target]]}.
                return result.IsUnit ? JsValue.Undefined : result;
            }

            var savedState = context.SaveCompletionState();

            GeneratorPendingCompletion? pending = null;
            var isGenerator = IsGeneratorContext(environment);
            if (isGenerator && savedState.HasCompletion)
            {
                pending = GetGeneratorPendingCompletion(environment);
                if (savedState.IsReturn)
                {
                    pending.HasValue = true;
                    pending.IsThrow = false;
                    pending.IsReturn = true;
                    // Store JsValue directly - will be boxed in object? but we handle unboxing later
                    pending.Value = savedState.ReturnValue;
                }
                else if (savedState.Signal is ThrowFlowCompletionSignal throwSignal)
                {
                    pending.HasValue = true;
                    pending.IsThrow = true;
                    pending.IsReturn = false;
                    // Store JsValue directly - will be boxed in object? but we handle unboxing later
                    pending.Value = throwSignal.JsValue;
                }
            }

            context.Clear();
            var finallyResult = EvaluateBlockJsValue(statement.Finally, environment, context);
            if (context.ShouldStopEvaluation)
            {
                // Per ES spec 14.15.2 TryStatement Evaluation:
                // Return Completion(UpdateEmpty(F, undefined)).
                // When finally has an abrupt completion, apply UpdateEmpty to its completion value.
                return finallyResult.IsUnit ? JsValue.Undefined : finallyResult;
            }

            if (isGenerator && pending?.HasValue == true)
            {
                // pending.Value might be a boxed JsValue
                var pendingValueJs = pending.Value is JsValue pjs ? pjs : JsValue.FromObjectUnsafe(pending.Value);

                if (pending.IsThrow)
                {
                    context.SetThrow(pendingValueJs);
                }
                else if (pending.IsReturn)
                {
                    context.SetReturn(pendingValueJs);
                }

                pending.HasValue = false;
                pending.IsThrow = false;
                pending.IsReturn = false;
                pending.Value = null;
            }
            else
            {
                context.RestoreCompletionState(savedState);
            }

            context.RealmState.Logger?.LogInformation(
                "EvaluateTry exit (with finally) throwFlag={ThrowFlag}",
                context.IsThrow);
            // Per ES spec 14.15.2 TryStatement Evaluation:
            // If C.[[value]] is not empty, return Completion(C).
            // Return Completion{[[type]]: C.[[type]], [[value]]: undefined, [[target]]: C.[[target]]}.
            return result.IsUnit ? JsValue.Undefined : result;
        }
    }
}
