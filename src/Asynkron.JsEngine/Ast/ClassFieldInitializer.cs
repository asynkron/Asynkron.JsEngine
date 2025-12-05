using System;
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

            constructorAccessor.SetProperty(propertyName, value);
            return true;
        }

        private static void SetAnonymousFunctionName(object? value, string displayName)
        {
            switch (value)
            {
                case TypedFunction typedFunction:
                    typedFunction.EnsureHasName(displayName);
                    break;
                case TypedGeneratorFactory generatorFactory:
                    generatorFactory.EnsureHasName(displayName);
                    break;
                case AsyncGeneratorFactory asyncGeneratorFactory:
                    asyncGeneratorFactory.EnsureHasName(displayName);
                    break;
            }
        }
    }
}
