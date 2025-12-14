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
        using var classFieldInitScope = context.EnterClassFieldInitializer();
        var initEnv = CreateStaticInitializationEnvironment(constructorAccessor, environment, out var superBinding);
        initEnv.Define(EvalHostFunction.FieldInitializerEvalFlag, true, isConst: true, isLexical: true,
            blocksFunctionScopeOverride: true);
        var resultValue = EvaluateExpression(expression, initEnv, context);
        var result = resultValue.ToObject();
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
        // Per ES spec, static blocks are evaluated like function bodies - var declarations
        // should be scoped to the block, not leak to outer environments
        var initEnv = new JsEnvironment(environment, isFunctionScope: true, isStrict: true);
        initEnv.Define(Symbol.This, constructorAccessor);
        // Field/static initializers are evaluated outside any constructor body; shadow new.target with undefined.
        initEnv.Define(Symbol.NewTarget, Symbol.Undefined, true, isLexical: true,
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
            prototypeValue.IsNullish)
        {
            return null;
        }

        var prototypeAccessor = prototypeValue.TryGetObject<IJsPropertyAccessor>(out var pa) ? pa : null;
        var superConstructor = prototypeValue.TryGetObject<IJsEnvironmentAwareCallable>(out var sc) ? sc : null;

        if (prototypeValue.IsNull)
        {
            return new SuperBinding(null, null, JsValue.FromObject(constructorAccessor), true);
        }

        if (prototypeAccessor is null && superConstructor is null)
        {
            return null;
        }

        return new SuperBinding(superConstructor, prototypeAccessor, JsValue.FromObject(constructorAccessor), true);
    }
}
