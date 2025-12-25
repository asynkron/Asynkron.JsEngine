#region

using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ForEachStatement statement)
    {
        private JsValue EvaluateForEachJsValue(JsEnvironment environment,
            EvaluationContext context, Symbol? loopLabel)
        {
            if (statement.Kind == ForEachKind.AwaitOf)
            {
                return statement.EvaluateForAwaitOfJsValue(environment, context, loopLabel);
            }

            var iterableEnvironment = environment;
            if (statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                or VariableKind.AwaitUsing)
            {
                // Create TDZ environment for the for-of head bindings.
                // NOTE: We do NOT pool this environment because closures in the iterable expression
                // may capture it, and returning it to the pool would break their scope chain.
                iterableEnvironment = new JsEnvironment(environment, false, false, statement.Source,
                    "for-each-head-tdz");
                var isConstDeclaration = statement.DeclarationKind is VariableKind.Const or VariableKind.Using
                    or VariableKind.AwaitUsing;
                statement.Target.CreateUninitializedLexicalBindings(iterableEnvironment, isConstDeclaration);
            }

            var iterableJsValue = statement.Iterable.EvaluateExpression(iterableEnvironment, context);

            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            if (statement.Kind == ForEachKind.Of)
            {
                // Check for null/undefined using JsValue.Kind
                if (iterableJsValue.IsNull || iterableJsValue.IsUndefined)
                {
                    throw StandardLibrary.ThrowTypeError("Cannot iterate over null or undefined", context,
                        context.RealmState);
                }
            }

            // In JavaScript, `for...in` requires an object value; iterating
            // over `null` or `undefined` throws a TypeError. Treat other
            // non-object values as errors as well so engine bugs surface
            // as JavaScript throws rather than host exceptions.
            if (statement.Kind == ForEachKind.In)
            {
                // Check based on Kind - Object kind covers IJsObjectLike, JsObject, JsArray
                // String kind is also iterable
                var kind = iterableJsValue.Kind;
                if (kind != JsValueKind.Object &&
                    kind != JsValueKind.String &&
                    kind != JsValueKind.Null &&
                    kind != JsValueKind.Undefined)
                {
                    throw new ThrowSignal("Cannot iterate properties of non-object value.");
                }
            }

            // Get the cached plan early - it contains pre-computed closure analysis
            var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();

            // Use pooled environment for loop scope only if no closures exist (cached on plan)
            var loopEnvironment = plan.CanPoolLoopEnvironment
                ? JsEnvironmentPool.Rent(environment, false, false, statement.Source, "for-each-loop")
                : new JsEnvironment(environment, false, false, statement.Source, "for-each-loop");

            context.RealmState.Logger?.LogDebug(
                "ForEach: CanPoolLoopEnvironment={CanPool}, CanReuseIterationEnvironment={CanReuse}",
                plan.CanPoolLoopEnvironment, plan.CanReuseIterationEnvironment);

            var lastValueJs = JsValue.Undefined;

            try
            {
            if (statement.Kind == ForEachKind.Of)
            {
                // FAST PATH: Use IEnumerator<JsValue> for known types (JsArray, TypedArray, string)
                // This avoids allocating iterator result objects {done, value} per iteration.
                var fastEnumerator = TryGetFastEnumeratorForIteration(iterableJsValue);
                if (fastEnumerator is not null)
                {
                    try
                    {
                        return plan.ExecuteIteratorDriverJsValue(null,
                            fastEnumerator,
                            loopEnvironment,
                            environment,
                            context,
                            loopLabel);
                    }
                    finally
                    {
                        fastEnumerator.Dispose();
                    }
                }

                // SLOW PATH: Full iterator protocol for custom iterables
                var iteratorTarget = NormalizeIterableTarget(iterableJsValue, context);
                if (TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) && iterator is not null)
                {
                    return plan.ExecuteIteratorDriverJsValue(iterator,
                        null,
                        loopEnvironment,
                        environment,
                        context,
                        loopLabel);
                }

                throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
            }

            var values = statement.Kind switch
            {
                ForEachKind.In => EnumeratePropertyKeys(iterableJsValue),
                _ => throw new ArgumentOutOfRangeException()
            };

            var hasSlotsConfigured = plan.IterationSlotCount >= 0 && plan.IterationScopeId >= 0;

            // For for-in with let/const, try to reuse a single iteration environment when no closures
            JsEnvironment? reusableIterationEnv = null;
            if (plan.CanReuseIterationEnvironment &&
                statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                    or VariableKind.AwaitUsing &&
                hasSlotsConfigured)
            {
                reusableIterationEnv = JsEnvironmentPool.Rent(loopEnvironment, false, false, statement.Source,
                    "for-each-iteration-reused");
                reusableIterationEnv.InitializeSlots(plan.IterationSlotCount, plan.IterationScopeId);
            }

            try
            {
            foreach (var value in values)
            {
                if (context.ShouldStopEvaluation)
                {
                    break;
                }

                // Use reusable environment if available, otherwise create per-iteration (not pooled due to closures)
                var iterationEnvironment = reusableIterationEnv ??
                    (statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                        or VariableKind.Using or VariableKind.AwaitUsing
                        ? CreateForInIterationEnvironment(loopEnvironment, plan, hasSlotsConfigured, statement.Source)
                        : loopEnvironment);

                statement.Target.AssignLoopBinding(value, iterationEnvironment, environment, context,
                    statement.DeclarationKind);

                IteratorDriverPlan.SyncIterationSlots(plan, iterationEnvironment, context);

                // Per ES spec 14.7.5.7 ForIn/OfBodyEvaluation step 5.k-l:
                // Only update V (completion value) if result.[[Value]] is not empty
                var bodyResult = statement.Body.EvaluateStatementJsValue(iterationEnvironment, context);
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
                    continue;
                }

                if (context.TryClearBreak(loopLabel))
                {
                    break;
                }
            }

            return lastValueJs;
            }
            finally
            {
                // Return reusable iteration environment to pool if we created one
                if (reusableIterationEnv is not null)
                {
                    JsEnvironmentPool.Return(reusableIterationEnv);
                }
            }
            }
            finally
            {
                // Only return to pool if we rented from pool (i.e., no closures)
                if (plan.CanPoolLoopEnvironment)
                {
                    JsEnvironmentPool.Return(loopEnvironment);
                }
            }
        }

        /// <summary>
        /// Creates a new iteration environment for for-in per-iteration scope.
        /// Used when closures exist and environment can't be reused/pooled.
        /// </summary>
        private static JsEnvironment CreateForInIterationEnvironment(
            JsEnvironment loopEnvironment,
            IteratorDriverPlan plan,
            bool hasSlotsConfigured,
            Parser.SourceReference? source)
        {
            // Don't pool since closures may capture this environment
            var env = new JsEnvironment(loopEnvironment, creatingSource: source,
                description: "for-each-iteration");
            if (hasSlotsConfigured)
            {
                env.InitializeSlots(plan.IterationSlotCount, plan.IterationScopeId);
            }
            return env;
        }

        private JsValue EvaluateForAwaitOfJsValue(JsEnvironment environment,
            EvaluationContext context, Symbol? loopLabel)
        {
            var iterableEnvironment = environment;
            if (statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                or VariableKind.AwaitUsing)
            {
                // Create TDZ environment for the for-await-of head bindings.
                // NOTE: We do NOT pool this environment because closures in the iterable expression
                // may capture it, and returning it to the pool would break their scope chain.
                iterableEnvironment = new JsEnvironment(environment, false, false, statement.Source,
                    "for-each-head-tdz");
                var isConstDeclaration = statement.DeclarationKind is VariableKind.Const or VariableKind.Using
                    or VariableKind.AwaitUsing;
                statement.Target.CreateUninitializedLexicalBindings(iterableEnvironment, isConstDeclaration);
            }

            var iterableJs = statement.Iterable.EvaluateExpression(iterableEnvironment, context);

            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            // Check for null/undefined using JsValue.Kind instead of ToObject()
            if (iterableJs.IsNull || iterableJs.IsUndefined)
            {
                throw StandardLibrary.ThrowTypeError("Cannot iterate over null or undefined", context,
                    context.RealmState);
            }

            // Get the cached plan early - it contains pre-computed closure analysis
            var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();

            // Use pooled environment for loop scope only if no closures exist (cached on plan)
            var loopEnvironment = plan.CanPoolLoopEnvironment
                ? JsEnvironmentPool.Rent(environment, false, false, statement.Source, "for-await-of loop")
                : new JsEnvironment(environment, false, false, statement.Source, "for-await-of loop");

            try
            {
            // FAST PATH: Use IEnumerator<JsValue> for sync iterables (arrays, typed arrays, strings)
            // This avoids iterator result object allocations while maintaining async semantics.
            var fastEnumerator = TryGetFastEnumeratorForIteration(iterableJs);
            if (fastEnumerator is not null)
            {
                try
                {
                    return plan.ExecuteIteratorDriverJsValue(null,
                        fastEnumerator,
                        loopEnvironment,
                        environment,
                        context,
                        loopLabel);
                }
                finally
                {
                    fastEnumerator.Dispose();
                }
            }

            // SLOW PATH: Full iterator protocol for custom async/sync iterables
            var iteratorTarget = NormalizeIterableTarget(iterableJs, context);
            if (TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) && iterator is not null)
            {
                return plan.ExecuteIteratorDriverJsValue(iterator,
                    null,
                    loopEnvironment,
                    environment,
                    context,
                    loopLabel);
            }

            throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
            }
            finally
            {
                // Only return to pool if we rented from pool (i.e., no closures)
                if (plan.CanPoolLoopEnvironment)
                {
                    JsEnvironmentPool.Return(loopEnvironment);
                }
            }
        }
    }
}
