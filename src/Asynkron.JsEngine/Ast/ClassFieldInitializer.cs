using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

internal static class ClassFieldInitializer
{
    extension(ImmutableArray<ClassField> fields)
    {
        public bool TryInitializeStaticFields(IJsPropertyAccessor constructorAccessor,
            Func<ExpressionNode, object?> evaluateExpression,
            EvaluationContext context,
            PrivateNameScope? privateNameScope,
            Func<IDisposable?>? privateScopeFactory)
        {
            foreach (var field in fields)
            {
                if (!field.TryResolveFieldName(evaluateExpression,
                        context,
                        privateNameScope,
                        out var propertyName))
                {
                    return false;
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
            }

            return true;
        }
    }
}
