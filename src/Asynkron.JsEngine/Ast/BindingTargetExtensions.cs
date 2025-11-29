namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(BindingTarget target)
    {
        private void AssignLoopBinding(object? value, JsEnvironment loopEnvironment,
            JsEnvironment outerEnvironment, EvaluationContext context, VariableKind? declarationKind)
        {
            if (declarationKind is null)
            {
                AssignBindingTarget(target, value, outerEnvironment, context);
                return;
            }

            switch (declarationKind)
            {
                case VariableKind.Var:
                    DefineOrAssignVar(target, value, loopEnvironment, context);
                    break;
                case VariableKind.Let:
                case VariableKind.Const:
                    DefineBindingTarget(target, value, loopEnvironment, context,
                        declarationKind == VariableKind.Const);
                    CollectSymbolsFromBinding(target, context.BlockedFunctionVarNames);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void CollectSymbolsFromBinding(HashSet<Symbol> names)
        {
            WalkBindingTargets(target, id => names.Add(id.Name));
        }

        private void HoistFromBindingTarget(JsEnvironment environment,
            EvaluationContext context,
            HashSet<Symbol>? lexicalNames = null)
        {
            WalkBindingTargets(target,
                identifier =>
                {
                    if (!context.CurrentScope.IsStrict && lexicalNames is not null &&
                        lexicalNames.Contains(identifier.Name))
                    {
                        return;
                    }

                    environment.DefineFunctionScoped(identifier.Name, Symbol.Undefined, false, context: context);
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

                            WalkBindingTargets(element.Target, onIdentifier);
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
                            WalkBindingTargets(property.Target, onIdentifier);
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

        private void AssignBindingTarget(object? value, JsEnvironment environment,
            EvaluationContext context)
        {
            ApplyBindingTarget(target, value, environment, context, BindingMode.Assign);
        }

        private void DefineBindingTarget(object? value, JsEnvironment environment,
            EvaluationContext context, bool isConst)
        {
            ApplyBindingTarget(target, value, environment, context,
                isConst ? BindingMode.DefineConst : BindingMode.DefineLet);
        }

        private void DefineOrAssignVar(object? value, JsEnvironment environment,
            EvaluationContext context)
        {
            ApplyBindingTarget(target, value, environment, context, BindingMode.DefineVar);
        }

        private void ApplyBindingTarget(object? value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode,
            bool hasInitializer = true,
            bool allowNameInference = true)
        {
            switch (target)
            {
                case IdentifierBinding identifier:
                    ApplyIdentifierBinding(identifier, value, environment, context, mode, hasInitializer,
                        allowNameInference);
                    break;
                case ArrayBinding arrayBinding:
                    BindArrayPattern(arrayBinding, value, environment, context, mode);
                    break;
                case ObjectBinding objectBinding:
                    BindObjectPattern(objectBinding, value, environment, context, mode);
                    break;
                case AssignmentTargetBinding assignmentTarget:
                {
                    var reference = AssignmentReferenceResolver.Resolve(
                        assignmentTarget.Expression,
                        environment,
                        context,
                        EvaluateExpression);
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
