#region

using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    internal static bool TryInitializeStaticField(this ResolvedClassField resolvedField, IJsPropertyAccessor constructorAccessor,
        JsEnvironment environment,
        EvaluationContext context,
        PrivateNameScope? privateNameScope,
        Func<IDisposable?>? privateScopeFactory)
    {
        var propertyName = resolvedField.Name;

        if (string.Equals(propertyName, "prototype", StringComparison.Ordinal))
        {
            throw StandardLibrary.ThrowTypeError("Cannot redefine constructor prototype via static member", context,
                context.RealmState);
        }

        if (resolvedField.IsPrivate && privateNameScope is not null && constructorAccessor is not IPrivateBrandHolder)
        {
            throw StandardLibrary.ThrowTypeError("Invalid private field receiver", context, context.RealmState);
        }

        var valueJs = JsValue.Undefined;
        if (resolvedField.InitializerProgram is { } initializerProgram)
        {
            using var handle = privateScopeFactory?.Invoke();
            using var classFieldInitScope = context.EnterClassFieldInitializer();
            var initEnv = CreateStaticInitializationEnvironment(constructorAccessor, environment, out var superBinding);
            initEnv.DefineJsValue(EvalHostFunction.FieldInitializerEvalFlag, JsValue.True, true, isLexicalBinding: true,
                blocksFunctionScopeOverride: true);
            valueJs = EvaluateLoweredExpressionProgram(
                initializerProgram,
                initEnv,
                context);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            if (valueJs.ObjectValue is SyncFunctionInvoker { IsArrowFunction: true } typedFunction &&
                superBinding is not null)
            {
                typedFunction.SetSuperBinding(superBinding.Constructor, superBinding.Prototype);
            }

            if (resolvedField.AnonymousFunctionName is { } displayName)
            {
                SetAnonymousFunctionName(valueJs, displayName);
            }
        }

        var descriptor = new PropertyDescriptor
        {
            JsValue = valueJs,
            Writable = true,
            Enumerable = true,
            Configurable = true
        };

        if (constructorAccessor is IPropertyDefinitionHost definitionHost)
        {
            if (!definitionHost.TryDefineProperty(propertyName, descriptor))
            {
                throw StandardLibrary.ThrowTypeError("Cannot define static class field", context,
                    context.RealmState);
            }
        }
        else if (constructorAccessor is IJsObjectLike objectLike)
        {
            objectLike.DefineProperty(propertyName, descriptor);
        }
        else
        {
            throw StandardLibrary.ThrowTypeError("Cannot define static class field", context, context.RealmState);
        }

        return true;
    }
}
