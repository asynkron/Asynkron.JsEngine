
#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static void AssignLoopBinding(this BindingTarget target, JsValue value, JsEnvironment loopEnvironment,
        JsEnvironment outerEnvironment, EvaluationContext context, VariableKind? declarationKind)
    {
        loopEnvironment.AssertOwnership(nameof(AssignLoopBinding));
        outerEnvironment.AssertOwnership(nameof(AssignLoopBinding));
        context.AssertOwnership(nameof(AssignLoopBinding));
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

    private static void CreateUninitializedLexicalBindings(this BindingTarget target, JsEnvironment environment, bool isConst)
    {
        // Fast path for simple identifier binding (most common case: `for (let x of arr)`)
        // Avoids Action<> delegate allocation
        if (target is IdentifierBinding id)
        {
            environment.DefineJsValue(id.Name, JsValue.Uninitialized, isConst,
                isLexicalBinding: true, blocksFunctionScopeOverride: true);
            return;
        }

        // Slow path for destructuring: `for (let {a, b} of arr)` or `for (let [x, y] of arr)`
        // Uses specialized walk that passes parameters directly to avoid lambda/display class allocation
        target.WalkAndDefineUninitializedBindings(environment, isConst);
    }

    /// <summary>
    /// Walks binding targets and defines uninitialized lexical bindings directly.
    /// Avoids Action delegate allocation by passing parameters explicitly.
    /// </summary>
    private static void WalkAndDefineUninitializedBindings(this BindingTarget target, JsEnvironment environment, bool isConst)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    environment.DefineJsValue(id.Name, JsValue.Uninitialized, isConst,
                        isLexicalBinding: true, blocksFunctionScopeOverride: true);
                    return;
                case ArrayBinding array:
                    foreach (var element in array.Elements)
                    {
                        element.Target?.WalkAndDefineUninitializedBindings(environment, isConst);
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
                        property.Target.WalkAndDefineUninitializedBindings(environment, isConst);
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

    /// <summary>
    /// Collects all symbol names from a binding target (identifier or destructuring pattern).
    /// Works with any collection type (List, HashSet, etc.) via ICollection interface.
    /// </summary>
    public static void CollectSymbolsFromBinding(this BindingTarget target, ICollection<Symbol> names)
    {
        // Fast path for simple identifier binding
        if (target is IdentifierBinding id)
        {
            names.Add(id.Name);
            return;
        }

        target.WalkBindingTargets(binding => names.Add(binding.Name));
    }

    private static void HoistFromBindingTarget(this BindingTarget target, JsEnvironment environment,
        EvaluationContext context,
        HashSet<Symbol>? lexicalNames = null)
    {
        // Fast path for simple identifier binding (most common case)
        if (target is IdentifierBinding id)
        {
            if (!context.CurrentScope.IsStrict && lexicalNames?.Contains(id.Name) == true)
            {
                return;
            }

            environment.DefineFunctionScoped(id.Name, JsValue.Undefined, false, context: context,
                canDelete: context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false });
            return;
        }

        // Slow path for destructuring patterns
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

    private static void WalkBindingTargets(this BindingTarget target, Action<IdentifierBinding> onIdentifier)
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

    private static void AssignBindingTarget(this BindingTarget target, JsValue value, JsEnvironment environment,
        EvaluationContext context)
    {
        target.ApplyBindingTarget(value, environment, context, BindingMode.Assign);
    }

    private static void DefineBindingTarget(this BindingTarget target, JsValue value, JsEnvironment environment,
        EvaluationContext context, bool isConst)
    {
        target.ApplyBindingTarget(value, environment, context,
            isConst ? BindingMode.DefineConst : BindingMode.DefineLet);
    }

    private static void DefineOrAssignVar(this BindingTarget target, JsValue value, JsEnvironment environment,
        EvaluationContext context)
    {
        target.ApplyBindingTarget(value, environment, context, BindingMode.DefineVar);
    }

    private static void ApplyBindingTarget(this BindingTarget target, JsValue value,
        JsEnvironment environment,
        EvaluationContext context,
        BindingMode mode,
        bool hasInitializer = true,
        bool allowNameInference = true,
        bool skipBlockedBindingLookup = false)
    {
        environment.AssertOwnership(nameof(ApplyBindingTarget));
        context.AssertOwnership(nameof(ApplyBindingTarget));
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
