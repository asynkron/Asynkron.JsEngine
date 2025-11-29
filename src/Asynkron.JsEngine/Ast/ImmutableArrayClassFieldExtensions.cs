using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ImmutableArray<ClassField> fields)
    {
        private void InitializeStaticFields(
            IJsPropertyAccessor constructorAccessor,
            JsEnvironment environment,
            EvaluationContext context,
            PrivateNameScope? privateNameScope)
        {
            using var staticFieldScope = context.PushScope(ScopeKind.Block, ScopeMode.Strict, true);
            Func<IDisposable?>? privateScopeFactory = privateNameScope is not null
                ? () => context.EnterPrivateNameScope(privateNameScope)
                : null;

            if (!fields.TryInitializeStaticFields(
                    constructorAccessor,
                    expr => EvaluateStaticFieldExpression(expr, constructorAccessor, environment, context),
                    context,
                    privateNameScope,
                    privateScopeFactory))
            {
            }
        }

    }

    private static object? EvaluateStaticFieldExpression(
        ExpressionNode expression,
        IJsPropertyAccessor constructorAccessor,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var initEnv = new JsEnvironment(environment, isStrict: true);
        initEnv.Define(Symbol.This, constructorAccessor);
        initEnv.Define(Symbol.NewTarget, constructorAccessor, true, isLexical: true,
            blocksFunctionScopeOverride: true);

        if (environment.TryGet(Symbol.Arguments, out var argumentsValue))
        {
            initEnv.Define(Symbol.Arguments, argumentsValue, isLexical: false);
        }

        var superBinding = ResolveStaticFieldSuperBinding(constructorAccessor);
        if (superBinding is not null)
        {
            initEnv.Define(Symbol.Super, superBinding, true, isLexical: true,
                blocksFunctionScopeOverride: true);
        }

        var result = EvaluateExpression(expression, initEnv, context);
        if (result is TypedFunction typedFunction &&
            typedFunction.IsArrowFunction &&
            superBinding is not null)
        {
            typedFunction.SetSuperBinding(superBinding.Constructor, superBinding.Prototype);
        }

        return result;
    }

    private static SuperBinding? ResolveStaticFieldSuperBinding(IJsPropertyAccessor constructorAccessor)
    {
        if (!constructorAccessor.TryGetProperty("__proto__", out var prototypeValue) ||
            ReferenceEquals(prototypeValue, Symbol.Undefined))
        {
            return null;
        }

        var prototypeAccessor = prototypeValue as IJsPropertyAccessor;
        var superConstructor = prototypeValue as IJsEnvironmentAwareCallable;

        if (prototypeValue is null)
        {
            return new SuperBinding(null, null, constructorAccessor, true);
        }

        if (prototypeAccessor is null && superConstructor is null)
        {
            return null;
        }

        return new SuperBinding(superConstructor, prototypeAccessor, constructorAccessor, true);
    }
}
