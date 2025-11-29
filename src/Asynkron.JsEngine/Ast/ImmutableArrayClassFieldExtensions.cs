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
                    expr => EvaluateExpression(expr, environment, context),
                    context,
                    privateNameScope,
                    privateScopeFactory))
            {
            }
        }
    }
}
