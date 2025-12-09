namespace Asynkron.JsEngine.Ast;

using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;

public static partial class TypedAstEvaluator
{
    extension(VariableKind kind)
    {
        private void EvaluateVariableDeclarator(VariableDeclarator declarator,
            JsEnvironment environment, EvaluationContext context)
        {
            var targetIdentifier = declarator.Target as IdentifierBinding;
            // Per ES spec 13.3.1.4: If IsAnonymousFunctionDefinition(Initializer) is true,
            // then perform SetFunctionName(value, bindingId).
            using var functionNameHint = targetIdentifier is not null &&
                                         declarator.Initializer is not null &&
                                         IsAnonymousFunctionDefinitionNode(declarator.Initializer)
                ? context.EnterFunctionNameHint(targetIdentifier.Name)
                : null;

            // Per ES spec 14.3.2.1: For var declarations, ResolveBinding happens BEFORE
            // evaluating the initializer. This is important for with statements where
            // the initializer might modify the with object (e.g., delete a property).
            // We pre-resolve the binding target to capture any with object reference.
            IJsObjectLike? preResolvedWithTarget = null;
            if (kind == VariableKind.Var && targetIdentifier is not null && declarator.Initializer is not null)
            {
                preResolvedWithTarget = environment.ResolveVarBindingWithTarget(targetIdentifier.Name);
            }

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

            // If we pre-resolved a with target for a var declaration, use it directly.
            // This ensures the binding goes to the with object even if the initializer
            // modified it (e.g., deleted the property).
            if (preResolvedWithTarget is not null && targetIdentifier is not null)
            {
                environment.AssignToWithTarget(preResolvedWithTarget, targetIdentifier.Name, value);
                return;
            }

            // Per ES spec 13.3.1.4: Name inference only applies if IsAnonymousFunctionDefinition(Initializer) is true
            var allowNameInference = declarator.Initializer is not null &&
                                    IsAnonymousFunctionDefinitionNode(declarator.Initializer);
            ApplyBindingTarget(declarator.Target, value, environment, context, mode,
                declarator.Initializer is not null, allowNameInference);
        }
    }
}
