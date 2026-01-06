#region

using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue EvaluateForEachJsValue(this ForEachStatement statement, JsEnvironment environment,
        EvaluationContext context, Symbol? loopLabel)
    {
        if (statement.Kind == ForEachKind.AwaitOf)
        {
            return statement.EvaluateForAwaitOfJsValue(environment, context, loopLabel);
        }

        var logger = context.RealmState.Logger;
        var cachedPlan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();

        var hasLexicalDeclaration = statement.DeclarationKind is VariableKind.Let or VariableKind.Const
            or VariableKind.Using or VariableKind.AwaitUsing;

        // Conditionally rent TDZ environment for lexical declarations
        using var pooledTdzEnv = hasLexicalDeclaration
            ? JsEnvironmentPool.Rent(environment, false, false, statement.Source, "for-each-head-tdz", logger: logger)
            : default;
        var iterableEnvironment = hasLexicalDeclaration ? pooledTdzEnv.Value! : environment;

        if (hasLexicalDeclaration)
        {
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

        // Pool handles captured environments (closures mark them, pool ignores on return)
        using var pooledLoopEnv = JsEnvironmentPool.Rent(environment, false, false, statement.Source, "for-each-loop", logger: logger);
        JsEnvironment loopEnvironment = pooledLoopEnv;

        if (statement.Kind == ForEachKind.Of)
        {
            return ExecuteIteratorWithFastPath(statement, iterableJsValue, loopEnvironment, environment, context, loopLabel);
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

        var lastValueJs = JsValue.Undefined;
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

            cachedPlan.SyncIterationSlots(iterationEnvironment, context);

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

    private static JsValue EvaluateForAwaitOfJsValue(this ForEachStatement statement, JsEnvironment environment,
        EvaluationContext context, Symbol? loopLabel)
    {
        var logger = context.RealmState.Logger;
        var hasLexicalDeclaration = statement.DeclarationKind is VariableKind.Let or VariableKind.Const
            or VariableKind.Using or VariableKind.AwaitUsing;

        // Conditionally rent TDZ environment for lexical declarations
        using var pooledTdzEnv = hasLexicalDeclaration
            ? JsEnvironmentPool.Rent(environment, false, false, statement.Source, "for-each-head-tdz", logger: logger)
            : default;
        var iterableEnvironment = hasLexicalDeclaration ? pooledTdzEnv.Value! : environment;

        if (hasLexicalDeclaration)
        {
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

        // Pool handles captured environments (closures mark them, pool ignores on return)
        using var pooledLoopEnv = JsEnvironmentPool.Rent(environment, false, false, statement.Source, "for-await-of loop",
            logger: logger);

        return ExecuteIteratorWithFastPath(statement, iterableJs, pooledLoopEnv, environment, context, loopLabel);
    }

    private static JsValue ExecuteIteratorWithFastPath(
        ForEachStatement statement,
        JsValue iterableValue,
        JsEnvironment loopEnvironment,
        JsEnvironment environment,
        EvaluationContext context,
        Symbol? loopLabel)
    {
        var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
        var useIterationSlots = plan is { IterationSlotCount: >= 0, IterationScopeId: >= 0 } &&
                                statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                                    or VariableKind.Using
                                    or VariableKind.AwaitUsing;

        // FAST PATH: Use IEnumerator<JsValue> for known types
        var fastEnumerator = TryGetFastEnumeratorForIteration(iterableValue);
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
        return ExecuteIteratorSlowPath(iterableValue, plan, loopEnvironment, environment, context, loopLabel, useIterationSlots);
    }

    private static JsValue ExecuteIteratorSlowPath(
        JsValue iterableValue,
        IteratorDriverPlan plan,
        JsEnvironment loopEnvironment,
        JsEnvironment environment,
        EvaluationContext context,
        Symbol? loopLabel,
        bool useIterationSlots)
    {
        var iteratorTarget = NormalizeIterableTarget(iterableValue, context);
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
}
