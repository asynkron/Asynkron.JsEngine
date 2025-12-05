namespace Asynkron.JsEngine.Ast;

using Microsoft.Extensions.Logging;

public static partial class TypedAstEvaluator
{
    extension(VariableKind kind)
    {
        private void EvaluateVariableDeclarator(VariableDeclarator declarator,
            JsEnvironment environment, EvaluationContext context)
        {
            var targetIdentifier = declarator.Target as IdentifierBinding;
            using var functionNameHint = declarator.Initializer is ClassExpression { Name: null } && targetIdentifier is not null
                ? context.EnterFunctionNameHint(targetIdentifier.Name)
                : null;

            var value = declarator.Initializer is null
                ? Symbol.Undefined
                : EvaluateExpression(declarator.Initializer, environment, context);

            if (context.ShouldStopEvaluation)
            {
                return;
            }

            var mode = kind switch
            {
                VariableKind.Var => BindingMode.DefineVar,
                VariableKind.Let => BindingMode.DefineLet,
                VariableKind.Const => BindingMode.DefineConst,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };

            if (context.RealmState.Logger is { } logger && targetIdentifier is not null)
            {
                logger.LogInformation(
                    "Initializing {Kind} binding '{Name}' (envDepth={Depth}, strict={Strict}) with value={Value}",
                    mode,
                    targetIdentifier.Name.Name,
                    environment.Depth,
                    environment.IsStrict,
                    value);
            }

            ApplyBindingTarget(declarator.Target, value, environment, context, mode,
                declarator.Initializer is not null);
        }
    }
}
