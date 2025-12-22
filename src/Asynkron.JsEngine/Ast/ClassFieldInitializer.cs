#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ClassField field)
    {
        public bool TryInitializeStaticField(
            IJsPropertyAccessor constructorAccessor,
            Func<ExpressionNode, JsValue> evaluateExpression,
            EvaluationContext context,
            PrivateNameScope? privateNameScope,
            Func<IDisposable?>? privateScopeFactory)
        {
            var propertyName = field.Name;

            if (string.Equals(propertyName, "prototype", StringComparison.Ordinal))
            {
                throw StandardLibrary.ThrowTypeError("Cannot redefine constructor prototype via static member", context,
                    context.RealmState);
            }

            if (field.IsPrivate && privateNameScope is not null && constructorAccessor is not IPrivateBrandHolder)
            {
                throw StandardLibrary.ThrowTypeError("Invalid private field receiver", context, context.RealmState);
            }

            var valueJs = JsValue.Undefined;
            var displayName = field.IsComputed ? propertyName : field.Name;
            var atIndex = displayName.IndexOf('@', StringComparison.Ordinal);
            if (atIndex > 0)
            {
                displayName = displayName[..atIndex];
            }

            if (field.Initializer is not null)
            {
                using var handle = privateScopeFactory?.Invoke();
                valueJs = evaluateExpression(field.Initializer);
                if (context.ShouldStopEvaluation)
                {
                    return false;
                }

                if (ExpressionNode.IsAnonymousFunctionDefinitionNode(field.Initializer))
                {
                    ClassField.SetAnonymousFunctionName(valueJs, displayName);
                }
            }

            var descriptor = new PropertyDescriptor
            {
                JsValue = valueJs, Writable = true, Enumerable = true, Configurable = true
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

        private static void SetAnonymousFunctionName(JsValue value, string displayName)
        {
            switch (value.ObjectValue)
            {
                case TypedFunction typedFunction:
                    typedFunction.EnsureHasName(displayName, true);
                    break;
                case TypedGeneratorFactory generatorFactory:
                    generatorFactory.EnsureHasName(displayName, true);
                    break;
                case AsyncGeneratorFactory asyncGeneratorFactory:
                    asyncGeneratorFactory.EnsureHasName(displayName, true);
                    break;
            }
        }
    }
}
