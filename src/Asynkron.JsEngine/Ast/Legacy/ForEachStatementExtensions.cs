#region

using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private const string DisableForOfLoopEnvPoolVar = "JSENGINE_DISABLE_FOROF_LOOP_ENV_POOL";

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
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
            ? JsEnvironmentPool.RentScoped(environment, false, false, statement.Source, "for-each-head-tdz", logger: logger)
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
            iterableJsValue = EvaluateCachedExpressionProgram(
                statement.Iterable,
                iterableEnvironment,
                context,
                "Dynamic foreach iterable");
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

        var useTypedArrayLoopEnv = statement.Kind == ForEachKind.Of && iterableJsValue.TryGetObject<TypedArrayBase>(out _);
        var useLoopPool = hasLexicalDeclaration && !useTypedArrayLoopEnv && !IsEnvEnabled(DisableForOfLoopEnvPoolVar);

        //TODO: are we renting an environment "just in case", if hasLexicalDeclaration is false, but useLoopPool is true?
        //TODO: this goes out of scope at the end, and is returned
        //TODO: but if other env is used, they are returned elsewhere?
        using var pooledLoopEnv = useLoopPool
            ? JsEnvironmentPool.RentScoped(environment, false, false, statement.Source, "for-each-loop", logger: logger)
            : default;
        var loopEnvironment = hasLexicalDeclaration switch
        {
            true => useLoopPool switch
            {
                true => pooledLoopEnv,
                _ => useTypedArrayLoopEnv
                    ? environment
                    : JsEnvironment.CreateInstance(environment, false, false, statement.Source, "for-each-loop")
            },
            _ => environment
        };

        if (statement.Kind == ForEachKind.Of)
        {
            return ExecuteIteratorWithFastPath(statement, iterableJsValue, loopEnvironment, environment, context, loopLabel);
        }

        var values = statement.Kind switch
        {
            ForEachKind.In => EnumeratePropertyKeys(iterableJsValue),
            ForEachKind.Of or ForEachKind.AwaitOf => throw new ArgumentOutOfRangeException(),
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
                iterationEnvironment = JsEnvironment.CreateInstance(loopEnvironment, creatingSource: statement.Source,
                    description: "for-each-iteration");
                if (useIterationSlotsForIn)
                {
                    InitializeIterationEnvironmentLayout(cachedPlan, iterationEnvironment);
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

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateForAwaitOfJsValue(this ForEachStatement statement, JsEnvironment environment,
        EvaluationContext context, Symbol? loopLabel)
    {
        var logger = context.RealmState.Logger;
        var hasLexicalDeclaration = statement.DeclarationKind is VariableKind.Let or VariableKind.Const
            or VariableKind.Using or VariableKind.AwaitUsing;

        // Conditionally rent TDZ environment for lexical declarations
        using var pooledTdzEnv = hasLexicalDeclaration
            ? JsEnvironmentPool.RentScoped(environment, false, false, statement.Source, "for-each-head-tdz", logger: logger)
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

        var iterableJs = EvaluateCachedExpressionProgram(
            statement.Iterable,
            iterableEnvironment,
            context,
            "Dynamic for-await-of iterable");

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

        var useLoopPool = hasLexicalDeclaration && !IsEnvEnabled(DisableForOfLoopEnvPoolVar);
        using var pooledLoopEnv = useLoopPool
            ? JsEnvironmentPool.RentScoped(environment, false, false, statement.Source, "for-await-of loop", logger: logger)
            : default;
        var loopEnvironment = hasLexicalDeclaration
            ? useLoopPool
                ? pooledLoopEnv.Value!
                : JsEnvironment.CreateInstance(environment, false, false, statement.Source, "for-await-of loop")
            : environment;

        return ExecuteIteratorWithFastPath(statement, iterableJs, loopEnvironment, environment, context, loopLabel);
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
        if (iterableValue.TryGetObject<TypedArrayBase>(out _))
        {
            useIterationSlots = false;
        }

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
