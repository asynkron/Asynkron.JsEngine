#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue EvaluateStaticFieldExpression(
        ExpressionNode expression,
        IJsPropertyAccessor constructorAccessor,
        JsEnvironment environment,
        EvaluationContext context)
    {
        using var classFieldInitScope = context.EnterClassFieldInitializer();
        var initEnv = CreateStaticInitializationEnvironment(constructorAccessor, environment, out var superBinding);
        initEnv.DefineJsValue(EvalHostFunction.FieldInitializerEvalFlag, JsValue.True, true, isLexical: true,
            blocksFunctionScopeOverride: true);
        var resultValue = expression.EvaluateExpression(initEnv, context);
        if (resultValue.ObjectValue is SyncFunctionInvoker { IsArrowFunction: true } typedFunction &&
            superBinding is not null)
        {
            typedFunction.SetSuperBinding(superBinding.Constructor, superBinding.Prototype);
        }

        return resultValue;
    }

    private static JsEnvironment CreateStaticInitializationEnvironment(
        IJsPropertyAccessor constructorAccessor,
        JsEnvironment environment,
        out SuperBinding? superBinding)
    {
        // Per ES spec, static blocks are evaluated like function bodies - var declarations
        // should be scoped to the block, not leak to outer environments
        var initEnv = new JsEnvironment(environment, true, true);
        initEnv.DefineJsValue(Symbol.This, JsValue.FromObjectUnsafe(constructorAccessor));
        // Field/static initializers are evaluated outside any constructor body; shadow new.target with undefined.
        initEnv.DefineJsValue(Symbol.NewTarget, JsValue.Undefined, true, isLexical: true,
            blocksFunctionScopeOverride: true);
        if (environment.TryGetJsValue(Symbol.Arguments, out var argumentsValue))
        {
            initEnv.DefineJsValue(Symbol.Arguments, argumentsValue, isLexical: false);
        }

        superBinding = ResolveStaticInitializationSuperBinding(constructorAccessor);
        if (superBinding is not null)
        {
            initEnv.DefineJsValue(Symbol.Super, JsValue.FromObjectUnsafe(superBinding), true, isLexical: true,
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
            return new SuperBinding(null, null, JsValue.FromObjectUnsafe(constructorAccessor), true);
        }

        if (prototypeAccessor is null && superConstructor is null)
        {
            return null;
        }

        return new SuperBinding(superConstructor, prototypeAccessor, JsValue.FromObjectUnsafe(constructorAccessor),
            true);
    }
}
