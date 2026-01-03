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
            var logger = context.RealmState.Logger;

            var cachedPlan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();

            var iterableEnvironment = environment;
            var pooledTdzEnvironment = false;
            var hasLexicalDeclaration = statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                or VariableKind.Using or VariableKind.AwaitUsing;
            if (hasLexicalDeclaration)
            {
                // Create TDZ environment for the for-of head bindings.
                // Pool this environment when there are no closures in target/iterable that could capture it.
                if (canPoolLoopEnvironment)
                {
                    iterableEnvironment = JsEnvironmentPool.Rent(environment, false, false, statement.Source,
                        "for-each-head-tdz", logger: logger);
                    pooledTdzEnvironment = true;
                }
                else
                {
                    iterableEnvironment = new JsEnvironment(environment, false, false, statement.Source,
                        "for-each-head-tdz");
                }

                InitializeIterationEnvironmentLayout(cachedPlan, iterableEnvironment);

                var isConstDeclaration = statement.DeclarationKind is VariableKind.Const or VariableKind.Using
                    or VariableKind.AwaitUsing;
                statement.Target.CreateUninitializedLexicalBindings(iterableEnvironment, isConstDeclaration);
            }

            var previousAllowIdentifierCache = context.AllowIdentifierCache;
            if (hasLexicalDeclaration)
            {
                // Disable identifier cache so the TDZ bindings in iterableEnvironment are respected.
                context.AllowIdentifierCache = false;
            }

            JsValue iterableJsValue;
            try
            {
                iterableJsValue = statement.Iterable.EvaluateExpression(iterableEnvironment, context);
            }
            finally
            {
                context.AllowIdentifierCache = previousAllowIdentifierCache;
            }

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
                ? JsEnvironmentPool.Rent(environment, false, false, statement.Source, "for-each-loop", logger: logger)
                : new JsEnvironment(environment, false, false, statement.Source, "for-each-loop");
            var lastValueJs = JsValue.Undefined;

            try
            {
                if (statement.Kind == ForEachKind.Of)
                {
                    var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
                    // OPTIMIZATION: Pass bool instead of Func<JsEnvironment> to avoid lambda allocation
                    var useIterationSlots = plan is { IterationSlotCount: >= 0, IterationScopeId: >= 0 } &&
                                            statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                                                or VariableKind.Using
                                                or VariableKind.AwaitUsing;

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
                                useIterationSlots);
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
                            useIterationSlots);
                    }

                    throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
                }

                var values = statement.Kind switch
                {
                    ForEachKind.In => EnumeratePropertyKeys(iterableJsValue),
                    _ => throw new ArgumentOutOfRangeException()
                };

                // OPTIMIZATION: Compute bool instead of allocating Func<JsEnvironment> lambda
                var useIterationSlotsForIn = cachedPlan is { IterationSlotCount: >= 0, IterationScopeId: >= 0 } &&
                                             statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                                                 or VariableKind.Using
                                                 or VariableKind.AwaitUsing;

                foreach (var value in values)
                {
                    if (context.ShouldStopEvaluation)
                    {
                        break;
                    }

                    // OPTIMIZATION: Inline environment creation to avoid lambda allocation
                    JsEnvironment iterationEnvironment;
                    if (statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                        or VariableKind.Using or VariableKind.AwaitUsing)
                    {
                        if (useIterationSlotsForIn)
                        {
                            iterationEnvironment = new JsEnvironment(loopEnvironment, creatingSource: statement.Source,
                                description: "for-each-iteration");
                            InitializeIterationEnvironmentLayout(cachedPlan, iterationEnvironment);
                        }
                        else
                        {
                            iterationEnvironment = new JsEnvironment(loopEnvironment, creatingSource: statement.Source,
                                description: "for-each-iteration");
                        }
                    }
                    else
                    {
                        iterationEnvironment = loopEnvironment;
                    }

                    statement.Target.AssignLoopBinding(value, iterationEnvironment, environment, context,
                        statement.DeclarationKind);

                    // Check if yield/await happened during binding (e.g., yield in destructuring default)
                    if (context.ShouldStopEvaluation)
                    {
                        break;
                    }

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
                // Return pooled environments
                if (pooledTdzEnvironment)
                {
                    JsEnvironmentPool.Return(iterableEnvironment, logger);
                }

                if (canPoolLoopEnvironment)
                {
                    JsEnvironmentPool.Return(loopEnvironment, logger);
                }
            }
        }

        private JsValue EvaluateForAwaitOfJsValue(JsEnvironment environment,
            EvaluationContext context, Symbol? loopLabel)
        {
            // Use cached analysis to check if loop environment can be pooled
            // (no closures in target or iterable that would capture it)
            var canPoolLoopEnvironment = statement.CanPoolLoopEnvironment;
            var logger = context.RealmState.Logger;

            var iterableEnvironment = environment;
            var pooledTdzEnvironment = false;
            if (statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                or VariableKind.AwaitUsing)
            {
                // Create TDZ environment for the for-await-of head bindings.
                // Pool this environment when there are no closures in target/iterable that could capture it.
                if (canPoolLoopEnvironment)
                {
                    iterableEnvironment = JsEnvironmentPool.Rent(environment, false, false, statement.Source,
                        "for-each-head-tdz", logger: logger);
                    pooledTdzEnvironment = true;
                }
                else
                {
                    iterableEnvironment = new JsEnvironment(environment, false, false, statement.Source,
                        "for-each-head-tdz");
                }

                var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
                InitializeIterationEnvironmentLayout(plan, iterableEnvironment);

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
                ? JsEnvironmentPool.Rent(environment, false, false, statement.Source, "for-await-of loop",
                    logger: logger)
                : new JsEnvironment(environment, false, false, statement.Source, "for-await-of loop");

            try
            {
                var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
                // OPTIMIZATION: Pass bool instead of Func<JsEnvironment> to avoid lambda allocation
                var useIterationSlots = plan is { IterationSlotCount: >= 0, IterationScopeId: >= 0 } &&
                                        statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                                            or VariableKind.Using
                                            or VariableKind.AwaitUsing;

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
                            useIterationSlots);
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
                        useIterationSlots);
                }

                throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
            }
            finally
            {
                // Return pooled environments
                if (pooledTdzEnvironment)
                {
                    JsEnvironmentPool.Return(iterableEnvironment, logger);
                }

                if (canPoolLoopEnvironment)
                {
                    JsEnvironmentPool.Return(loopEnvironment, logger);
                }
            }
        }
    }
}
