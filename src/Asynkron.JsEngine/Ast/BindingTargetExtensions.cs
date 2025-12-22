#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(BindingTarget target)
    {
        private void AssignLoopBinding(JsValue value, JsEnvironment loopEnvironment,
            JsEnvironment outerEnvironment, EvaluationContext context, VariableKind? declarationKind)
        {
            if (declarationKind is null)
            {
                target.AssignBindingTarget(value, outerEnvironment, context);
                return;
            }

            switch (declarationKind)
            {
                case VariableKind.Var:
                    target.DefineOrAssignVar(value, loopEnvironment, context);
                    break;
                case VariableKind.Let:
                case VariableKind.Const:
                case VariableKind.Using:
                case VariableKind.AwaitUsing:
                    target.DefineBindingTarget(value, loopEnvironment, context,
                        declarationKind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void CreateUninitializedLexicalBindings(JsEnvironment environment, bool isConst)
        {
            target.WalkBindingTargets(id => environment.DefineJsValue(id.Name, JsValue.Uninitialized, isConst,
                isLexical: true, blocksFunctionScopeOverride: true));
        }

        private void CollectSymbolsFromBinding(HashSet<Symbol> names)
        {
            target.WalkBindingTargets(id => names.Add(id.Name));
        }

        private void HoistFromBindingTarget(JsEnvironment environment,
            EvaluationContext context,
            HashSet<Symbol>? lexicalNames = null)
        {
            target.WalkBindingTargets(identifier =>
            {
                if (!context.CurrentScope.IsStrict && lexicalNames?.Contains(identifier.Name) == true)
                {
                    return;
                }

                environment.DefineFunctionScoped(identifier.Name, JsValue.Undefined, false, context: context,
                    canDelete: context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false });
            });
        }

        private void WalkBindingTargets(Action<IdentifierBinding> onIdentifier)
        {
            while (true)
            {
                switch (target)
                {
                    case IdentifierBinding id:
                        onIdentifier(id);
                        return;
                    case ArrayBinding array:
                        foreach (var element in array.Elements)
                        {
                            if (element.Target is null)
                            {
                                continue;
                            }

                            element.Target.WalkBindingTargets(onIdentifier);
                        }

                        if (array.RestElement is null)
                        {
                            return;
                        }

                        target = array.RestElement;
                        continue;

                    case ObjectBinding obj:
                        foreach (var property in obj.Properties)
                        {
                            property.Target.WalkBindingTargets(onIdentifier);
                        }

                        if (obj.RestElement is null)
                        {
                            return;
                        }

                        target = obj.RestElement;
                        continue;

                    default:
                        return;
                }
            }
        }

        private void AssignBindingTarget(JsValue value, JsEnvironment environment,
            EvaluationContext context)
        {
            target.ApplyBindingTarget(value, environment, context, BindingMode.Assign);
        }

        private void DefineBindingTarget(JsValue value, JsEnvironment environment,
            EvaluationContext context, bool isConst)
        {
            target.ApplyBindingTarget(value, environment, context,
                isConst ? BindingMode.DefineConst : BindingMode.DefineLet);
        }

        private void DefineOrAssignVar(JsValue value, JsEnvironment environment,
            EvaluationContext context)
        {
            target.ApplyBindingTarget(value, environment, context, BindingMode.DefineVar);
        }

        private void ApplyBindingTarget(JsValue value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode,
            bool hasInitializer = true,
            bool allowNameInference = true,
            bool skipBlockedBindingLookup = false)
        {
            switch (target)
            {
                case IdentifierBinding identifier:
                    identifier.ApplyIdentifierBinding(value, environment, context, mode, hasInitializer,
                        allowNameInference, skipBlockedBindingLookup);
                    break;
                case ArrayBinding arrayBinding:
                    arrayBinding.BindArrayPattern(value, environment, context, mode);
                    break;
                case ObjectBinding objectBinding:
                    objectBinding.BindObjectPattern(value, environment, context, mode);
                    break;
                case AssignmentTargetBinding assignmentTarget:
                {
                    // Use fast path for identifiers, slow path for member expressions
                    var reference = assignmentTarget.Expression is IdentifierExpression
                        ? AssignmentReferenceResolver.ResolveIdentifierFast(
                            assignmentTarget.Expression, environment, context)
                        : AssignmentReferenceResolver.Resolve(
                            assignmentTarget.Expression,
                            environment,
                            context,
                            static (e, env, ctx) => e.EvaluateExpression(env, ctx));
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }

                    reference.SetValue(value);
                    break;
                }
                default:
                    throw new NotSupportedException($"Binding target '{target.GetType().Name}' is not supported.");
            }
        }
    }
}
