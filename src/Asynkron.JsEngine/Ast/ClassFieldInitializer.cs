using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

internal static class ClassFieldInitializer
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
            if (!field.TryResolveFieldName(evaluateExpression, context, privateNameScope, out var propertyName))
            {
                return false;
            }

            if (string.Equals(propertyName, "prototype", StringComparison.Ordinal))
            {
                throw StandardLibrary.ThrowTypeError("Cannot redefine constructor prototype via static member", context,
                    context.RealmState);
            }

            object? value = Symbol.Undefined;
            if (field.Initializer is not null)
            {
                using var handle = privateScopeFactory?.Invoke();
                value = evaluateExpression(field.Initializer);
                if (context.ShouldStopEvaluation)
                {
                    return false;
                }
            }

            constructorAccessor.SetProperty(propertyName, value);
            return true;
        }
    }
}
