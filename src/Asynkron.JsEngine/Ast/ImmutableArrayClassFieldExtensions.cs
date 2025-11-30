using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static object? EvaluateStaticFieldExpression(
        ExpressionNode expression,
        IJsPropertyAccessor constructorAccessor,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var initEnv = CreateStaticInitializationEnvironment(constructorAccessor, environment, out var superBinding);
        var result = EvaluateExpression(expression, initEnv, context);
        if (result is TypedFunction typedFunction &&
            typedFunction.IsArrowFunction &&
            superBinding is not null)
        {
            typedFunction.SetSuperBinding(superBinding.Constructor, superBinding.Prototype);
        }

        return result;
    }

    private static JsEnvironment CreateStaticInitializationEnvironment(
        IJsPropertyAccessor constructorAccessor,
        JsEnvironment environment,
        out SuperBinding? superBinding)
    {
        var initEnv = new JsEnvironment(environment, isStrict: true);
        initEnv.Define(Symbol.This, constructorAccessor);
        initEnv.Define(Symbol.NewTarget, constructorAccessor, true, isLexical: true,
            blocksFunctionScopeOverride: true);

        if (environment.TryGet(Symbol.Arguments, out var argumentsValue))
        {
            initEnv.Define(Symbol.Arguments, argumentsValue, isLexical: false);
        }

        superBinding = ResolveStaticInitializationSuperBinding(constructorAccessor);
        if (superBinding is not null)
        {
            initEnv.Define(Symbol.Super, superBinding, true, isLexical: true,
                blocksFunctionScopeOverride: true);
        }

        return initEnv;
    }

    private static SuperBinding? ResolveStaticInitializationSuperBinding(IJsPropertyAccessor constructorAccessor)
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
