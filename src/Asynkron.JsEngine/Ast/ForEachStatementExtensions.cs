#region

using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

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

            // Use cached analysis to check if loop environment can be pooled
            // (no closures in target or iterable that would capture it)
            var canPoolLoopEnvironment = statement.CanPoolLoopEnvironment;

            var iterableEnvironment = environment;
            if (statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                or VariableKind.AwaitUsing)
            {
                // Create TDZ environment for the for-of head bindings.
                // NOTE: We do NOT pool this environment because it may be captured across yield/await
                // boundaries or closures in the iterable expression.
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

            // Use pooled environment for loop scope only if no closures will capture the chain
            var loopEnvironment = canPoolLoopEnvironment
                ? JsEnvironmentPool.Rent(environment, false, false, statement.Source, "for-each-loop")
                : new JsEnvironment(environment, false, false, statement.Source, "for-each-loop");
            var lastValueJs = JsValue.Undefined;

            try
            {
            if (statement.Kind == ForEachKind.Of)
            {
                var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
                Func<JsEnvironment>? rentIterationEnvironment = null;
                if (plan is { IterationSlotCount: >= 0, IterationScopeId: >= 0 } &&
                    statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                        or VariableKind.AwaitUsing)
                {
                    rentIterationEnvironment = () =>
                    {
                        var env = JsEnvironmentPool.Rent(loopEnvironment, false, false, statement.Source,
                            "for-each-iteration");
                        env.InitializeSlots(plan.IterationSlotCount, plan.IterationScopeId);
                        return env;
                    };
                }

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
                            loopLabel,
                            rentIterationEnvironment);
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
                        loopLabel,
                        rentIterationEnvironment);
                }

                throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
            }

            var values = statement.Kind switch
            {
                ForEachKind.In => EnumeratePropertyKeys(iterableJsValue),
                _ => throw new ArgumentOutOfRangeException()
            };

            Func<JsEnvironment>? rentLoopIterationEnv = null;
            var cachedPlan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
            if (cachedPlan is { IterationSlotCount: >= 0, IterationScopeId: >= 0 } &&
                statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                    or VariableKind.AwaitUsing)
            {
                rentLoopIterationEnv = () =>
                {
                    var env = JsEnvironmentPool.Rent(loopEnvironment, false, false, statement.Source,
                        "for-each-iteration");
                    env.InitializeSlots(cachedPlan.IterationSlotCount, cachedPlan.IterationScopeId);
                    return env;
                };
            }

            foreach (var value in values)
            {
                if (context.ShouldStopEvaluation)
                {
                    break;
                }

                var iterationEnvironment = statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                    or VariableKind.Using or VariableKind.AwaitUsing
                    ? rentLoopIterationEnv is not null
                        ? rentLoopIterationEnv()
                        : new JsEnvironment(loopEnvironment, creatingSource: statement.Source,
                            description: "for-each-iteration")
                    : loopEnvironment;

                statement.Target.AssignLoopBinding(value, iterationEnvironment, environment, context,
                    statement.DeclarationKind);

                IteratorDriverPlan.SyncIterationSlots(cachedPlan, iterationEnvironment, context);

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
                // Only return loop environment to pool if we pooled it
                if (canPoolLoopEnvironment)
                {
                    JsEnvironmentPool.Return(loopEnvironment);
                }
            }
        }

        private JsValue EvaluateForAwaitOfJsValue(JsEnvironment environment,
            EvaluationContext context, Symbol? loopLabel)
        {
            // Use cached analysis to check if loop environment can be pooled
            // (no closures in target or iterable that would capture it)
            var canPoolLoopEnvironment = statement.CanPoolLoopEnvironment;

            var iterableEnvironment = environment;
            if (statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                or VariableKind.AwaitUsing)
            {
                // Create TDZ environment for the for-await-of head bindings.
                // NOTE: We do NOT pool this environment because it may be captured across yield/await
                // boundaries or closures in the iterable expression.
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

            // Use pooled environment for loop scope only if no closures will capture the chain
            var loopEnvironment = canPoolLoopEnvironment
                ? JsEnvironmentPool.Rent(environment, false, false, statement.Source, "for-await-of loop")
                : new JsEnvironment(environment, false, false, statement.Source, "for-await-of loop");

            try
            {
            var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
            Func<JsEnvironment>? rentIterationEnvironment = null;
            if (plan is { IterationSlotCount: >= 0, IterationScopeId: >= 0 } &&
                statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                    or VariableKind.AwaitUsing)
            {
                rentIterationEnvironment = () =>
                {
                    var env = JsEnvironmentPool.Rent(loopEnvironment, false, false, statement.Source,
                        "for-each-iteration");
                    env.InitializeSlots(plan.IterationSlotCount, plan.IterationScopeId);
                    return env;
                };
            }

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
                        loopLabel,
                        rentIterationEnvironment);
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
                    loopLabel,
                    rentIterationEnvironment);
            }

            throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
            }
            finally
            {
                // Only return loop environment to pool if we pooled it
                if (canPoolLoopEnvironment)
                {
                    JsEnvironmentPool.Return(loopEnvironment);
                }
            }
        }
    }
}
