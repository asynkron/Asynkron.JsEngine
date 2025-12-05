namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(VariableKind kind)
    {
        private void EvaluateVariableDeclarator(VariableDeclarator declarator,
            JsEnvironment environment, EvaluationContext context)
        {
            using var functionNameHint = declarator.Initializer is ClassExpression { Name: null }
                                         && declarator.Target is IdentifierBinding identifier
                ? context.EnterFunctionNameHint(identifier.Name)
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

            ApplyBindingTarget(declarator.Target, value, environment, context, mode,
                declarator.Initializer is not null);
        }
    }
}
