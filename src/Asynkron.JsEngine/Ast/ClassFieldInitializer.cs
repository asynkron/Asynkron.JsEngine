using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ClassField field)
    {
        public bool TryInitializeStaticField(
            IJsPropertyAccessor constructorAccessor,
            Func<ExpressionNode, object?> evaluateExpression,
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

            object? value = Symbol.Undefined;
            var displayName = field.IsComputed ? propertyName : field.Name;
            var atIndex = displayName.IndexOf('@');
            if (atIndex > 0)
            {
                displayName = displayName[..atIndex];
            }
            if (field.Initializer is not null)
            {
                using var handle = privateScopeFactory?.Invoke();
                value = evaluateExpression(field.Initializer);
                if (context.ShouldStopEvaluation)
                {
                    return false;
                }

                if (IsAnonymousFunctionDefinitionNode(field.Initializer))
                {
                    SetAnonymousFunctionName(value, displayName);
                }
            }

            var descriptor = new PropertyDescriptor
            {
                Value = value,
                Writable = true,
                Enumerable = true,
                Configurable = true
            };

            if (constructorAccessor is IPropertyDefinitionHost definitionHost)
            {
                if (!definitionHost.TryDefineProperty(propertyName, descriptor))
                {
                    throw StandardLibrary.ThrowTypeError("Cannot define static class field", context, context.RealmState);
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

        private static void SetAnonymousFunctionName(object? value, string displayName)
        {
            switch (value)
            {
                case TypedFunction typedFunction:
                    typedFunction.EnsureHasName(displayName, overwriteExisting: true);
                    break;
                case TypedGeneratorFactory generatorFactory:
                    generatorFactory.EnsureHasName(displayName, overwriteExisting: true);
                    break;
                case AsyncGeneratorFactory asyncGeneratorFactory:
                    asyncGeneratorFactory.EnsureHasName(displayName, overwriteExisting: true);
                    break;
            }
        }
    }
}
