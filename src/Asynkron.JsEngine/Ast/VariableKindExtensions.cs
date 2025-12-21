namespace Asynkron.JsEngine.Ast;

using Microsoft.Extensions.Logging;
using JsTypes;
using StdLib;

public static partial class TypedAstEvaluator
{
    extension(VariableKind kind)
    {
        private void EvaluateVariableDeclarator(VariableDeclarator declarator,
            JsEnvironment environment, EvaluationContext context)
        {
            var targetIdentifier = declarator.Target as IdentifierBinding;
            AssignmentReference? preResolvedVarReference = null;

            // Var declarations resolve their binding before evaluating the initializer so
            // `with` environments/proxies see the correct lookup order (ResolveBinding step).
            if (kind == VariableKind.Var &&
                declarator.Initializer is not null &&
                targetIdentifier is not null)
            {
                // Var bindings are hoisted to the nearest function scope before evaluating
                // the initializer so resolution targets the correct environment.
                environment.EnsureFunctionScopedVarBinding(targetIdentifier.Name, context);

                var identifierExpression = new IdentifierExpression(declarator.Source, targetIdentifier.Name);
                // Use fast path - this is always an identifier expression
                preResolvedVarReference = AssignmentReferenceResolver.ResolveIdentifierFast(
                    identifierExpression,
                    environment,
                    context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }
            // Per ES spec 13.3.1.4: If IsAnonymousFunctionDefinition(Initializer) is true,
            // then perform SetFunctionName(value, bindingId).
            using var functionNameHint = targetIdentifier is not null &&
                                         declarator.Initializer is not null && ExpressionNode.IsAnonymousFunctionDefinitionNode(declarator.Initializer)
                ? context.EnterFunctionNameHint(targetIdentifier.Name)
                : null;

            var valueJs = declarator.Initializer is null
                ? JsValue.Undefined
                : declarator.Initializer.EvaluateExpression(environment, context);

            if (context.ShouldStopEvaluation)
            {
                return;
            }

            if (kind is VariableKind.Using or VariableKind.AwaitUsing)
            {
                if (!valueJs.IsNullOrUndefined && !valueJs.TryGetObject<IJsObjectLike>(out _))
                {
                    throw StandardLibrary.ThrowTypeError(
                        "using declarations require an object value",
                        context,
                        context.RealmState);
                }

                if (valueJs.TryGetObject<IJsObjectLike>(out _))
                {
                    throw new NotSupportedException(
                        "Explicit resource management is not implemented for object resources.");
                }
            }

            var mode = kind switch
            {
                VariableKind.Var => BindingMode.DefineVar,
                VariableKind.Let => BindingMode.DefineLet,
                VariableKind.Const => BindingMode.DefineConst,
                VariableKind.Using => BindingMode.DefineConst,
                VariableKind.AwaitUsing => BindingMode.DefineConst,
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
                    valueJs);
            }

            if (preResolvedVarReference is { } resolvedReference && targetIdentifier is not null)
            {
                var assignedBlockedBinding = environment.TryAssignBlockedBindingJsValue(targetIdentifier.Name, valueJs);
                environment.EnsureFunctionScopedVarBinding(targetIdentifier.Name, context);
                if (!assignedBlockedBinding)
                {
                    resolvedReference.SetValue(valueJs);
                }

                return;
            }

            // Per ES spec 13.3.1.4: Name inference only applies if IsAnonymousFunctionDefinition(Initializer) is true
            var allowNameInference = declarator.Initializer is not null && ExpressionNode.IsAnonymousFunctionDefinitionNode(declarator.Initializer);
            declarator.Target.ApplyBindingTargetJsValue(valueJs, environment, context, mode,
                declarator.Initializer is not null, allowNameInference);
        }
    }
}
