using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(TryStatement statement)
    {
        private object? EvaluateTry(JsEnvironment environment, EvaluationContext context)
        {
            return EvaluateTryJsValue(statement, environment, context).ToObject();
        }

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
                return result;
            }

            var savedSignal = context.CurrentSignal;

            GeneratorPendingCompletion? pending = null;
            var isGenerator = IsGeneratorContext(environment);
            if (isGenerator && savedSignal is not null)
            {
                pending = GetGeneratorPendingCompletion(environment);
                switch (savedSignal)
                {
                    case ThrowFlowCompletionSignal throwSignal:
                        pending.HasValue = true;
                        pending.IsThrow = true;
                        pending.IsReturn = false;
                        pending.Value = throwSignal.JsValue;
                        break;
                    case ReturnCompletionSignal returnSignal:
                        pending.HasValue = true;
                        pending.IsThrow = false;
                        pending.IsReturn = true;
                        pending.Value = returnSignal.JsValue;
                        break;
                }
            }

            context.Clear();
            var finallyResult = EvaluateBlockJsValue(statement.Finally, environment, context);
            if (context.CurrentSignal is not null)
            {
                // Per ES spec: When finally has an abrupt completion, use its completion value
                return finallyResult;
            }

            if (isGenerator && pending?.HasValue == true)
            {
                if (pending.IsThrow)
                {
                    context.SetThrow(JsValue.FromObject(pending.Value));
                }
                else if (pending.IsReturn)
                {
                    context.SetReturn(JsValue.FromObject(pending.Value));
                }

                pending.HasValue = false;
                pending.IsThrow = false;
                pending.IsReturn = false;
                pending.Value = null;
            }
            else
            {
                RestoreSignal(context, savedSignal);
            }

            context.RealmState.Logger?.LogInformation(
                "EvaluateTry exit (with finally) throwFlag={ThrowFlag}",
                context.IsThrow);
            return result;
        }
    }
}
